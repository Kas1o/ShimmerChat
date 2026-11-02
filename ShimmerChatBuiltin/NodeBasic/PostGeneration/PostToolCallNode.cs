using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    /// <summary>工具结果以什么身份写入对话。</summary>
    public enum PostToolResultSender
    {
        /// <summary>作为 tool_result 消息（标准做法）。</summary>
        ToolResult,
        /// <summary>作为用户消息。</summary>
        User,
        /// <summary>作为系统消息。</summary>
        System
    }

    /// <summary>本轮没有工具调用时的循环行为。</summary>
    public enum PostNoToolCallBehavior
    {
        /// <summary>不改动循环指令（默认 Stop，即结束生成）。</summary>
        LeaveDirective,
        /// <summary>强制再生成一轮。</summary>
        Continue
    }

    /// <summary>
    /// 执行本轮 LLM 请求的工具调用，把结果写回对话并继续下一轮。
    /// 后处理管线接管工具调用的位置就是这里——它不在，工具调用就不会被执行。
    /// <para>
    /// 工具支持两代接口：<see cref="IToolV3"/>（结构化结果 + 取消）优先，
    /// 旧 <see cref="IToolV2"/> 的字符串结果被包装成 <see cref="ToolResult"/>，行为与之前一致。
    /// 工具内部等待用户输入（如 ask_user）不需要本节点配合：那段等待发生在
    /// <c>ExecuteAsync</c> 内部，本节点只是"一次调用耗时较长"。
    /// </para>
    /// </summary>
    [NodeInfo("node.post_tool_call",
        Icon = "🔧",
        Color = "var(--node-tool)",
        CategoryKeys = ["category.post", "category.tool"],
        DescriptionKey = "node.post_tool_call.desc")]
    public class PostToolCallNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Tool Calls";

        [NodeProperty("prop.post_tool_call.continue_on_error",
            HintKey = "prop.post_tool_call.continue_on_error.hint", Order = 10)]
        public bool ContinueOnError { get; set; } = true;

        [NodeProperty("prop.post_tool_call.max_calls",
            HintKey = "prop.post_tool_call.max_calls.hint", Order = 20)]
        public int MaxCallsPerRound { get; set; }

        [NodeProperty("prop.post_tool_call.emit_sender",
            HintKey = "prop.post_tool_call.emit_sender.hint", Order = 30)]
        public PostToolResultSender EmitSender { get; set; } = PostToolResultSender.ToolResult;

        [NodeProperty("prop.post_tool_call.no_tool_call",
            HintKey = "prop.post_tool_call.no_tool_call.hint", Order = 40)]
        public PostNoToolCallBehavior NoToolCallBehavior { get; set; } = PostNoToolCallBehavior.LeaveDirective;

        /// <summary>一次工具调用的执行结论：要么有内容可写，要么是一个真相失败。</summary>
        private readonly record struct ToolCallOutcome(string? Content, string? FailCode, string? FailMessage);

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            var env = context.Env;
            var toolCalls = env.ResponseMessage.toolCalls;

            if (toolCalls == null || toolCalls.Count == 0)
            {
                if (NoToolCallBehavior == PostNoToolCallBehavior.Continue)
                    env.LoopDirective = PostLoopDirective.Continue;
                return PostNodeResult.SuccessResult();
            }

            env.ToolCallsHandled = true;

            var calls = MaxCallsPerRound > 0
                ? toolCalls.Take(MaxCallsPerRound).ToList()
                : toolCalls;

            if (calls.Count < toolCalls.Count)
            {
                // 被丢弃的调用不会产生 tool_result，历史完整性由 AppendChatMessagesNode 的
                // DanglingToolCalls 策略兜底（默认注入占位），这里只留痕。
                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: {toolCalls.Count - calls.Count} tool call(s) dropped by MaxCallsPerRound={MaxCallsPerRound}.");
            }

            foreach (var toolCall in calls)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var tool = env.Tools.FirstOrDefault(t => t.GetDefinition().name == toolCall.name);
                var outcome = await ExecuteOneAsync(env, tool, toolCall, context.CancellationToken);

                if (outcome.FailCode != null)
                    return Fail(outcome.FailCode, outcome.FailMessage ?? "Tool call failed.");

                // 工具执行期间可能发生取消（尤其是等待用户输入）：写库前再检查一次，
                // 避免把"半轮工具结果"落进对话。
                context.CancellationToken.ThrowIfCancellationRequested();

                PostChat.Append(env,
                    new ChatMessage { Content = outcome.Content ?? string.Empty, id = toolCall.id },
                    ResolveSender());
            }

            // 本轮产生了工具结果，模型需要看到它们才能继续（或收尾）
            env.LoopDirective = PostLoopDirective.Continue;
            return PostNodeResult.SuccessResult();
        }

        /// <summary>执行一次调用。返回 FailCode 表示硬失败（节点据此中止本轮）。</summary>
        private async Task<ToolCallOutcome> ExecuteOneAsync(PostGenerationEnv env, IToolV2? tool,
            ToolCall toolCall, CancellationToken ct)
        {
            if (tool == null)
            {
                if (!ContinueOnError)
                    return new ToolCallOutcome(null, NodeErrorCodes.ToolNotFound,
                        $"Tool '{toolCall.name}' is not available in the current round.");

                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: tool '{toolCall.name}' not found, reporting to the model.");
                return new ToolCallOutcome($"[Tool error] Tool '{toolCall.name}' not found.", null, null);
            }

            try
            {
                var result = await InvokeAsync(env, tool, toolCall, ct);
                return Interpret(env, toolCall, result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户 / 宿主取消：绝不吞成"工具错误"，否则用户按了停止还会继续下一轮。
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // ct 未取消却抛取消 = 工具内部超时（如 HttpClient.Timeout），这是真的失败。
                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: tool '{toolCall.name}' was cancelled internally: {ex.Message}");
                if (!ContinueOnError)
                    throw;
                return new ToolCallOutcome("[Tool error] The tool timed out.", null, null);
            }
            catch (Exception ex) when (ContinueOnError)
            {
                // 吞异常必须留痕：模型看到的是错误文本，排查依赖 DebugOutput
                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: tool '{toolCall.name}' threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return new ToolCallOutcome($"[Tool error] {ex.Message}", null, null);
            }
        }

        /// <summary>按接口代次分派：优先结构化接口，旧接口的字符串结果包装成 <see cref="ToolResult"/>。</summary>
        private static async Task<ToolResult> InvokeAsync(PostGenerationEnv env, IToolV2 tool,
            ToolCall toolCall, CancellationToken ct)
        {
            if (tool is IToolV3 v3)
            {
                return await v3.ExecuteAsync(new ToolInvocationContext
                {
                    ToolCallId = toolCall.id ?? string.Empty,
                    ToolName = toolCall.name,
                    ArgumentsJson = toolCall.arguments ?? "{}",
                    RoundIndex = env.RoundIndex
                }, ct);
            }

            var text = await tool.ExecuteAsync(toolCall.arguments ?? "{}");
            return ToolResult.Ok(text);
        }

        /// <summary>把工具结果翻译成"写给模型的内容"或硬失败。</summary>
        private ToolCallOutcome Interpret(PostGenerationEnv env, ToolCall toolCall, ToolResult result)
        {
            if (result.Status == ToolResultStatus.Completed)
                return new ToolCallOutcome(result.Content ?? string.Empty, null, null);

            env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                $"Round {env.RoundIndex}: tool '{toolCall.name}' failed ({result.ErrorCode}): " +
                $"{result.ErrorMessage}{(result.Details == null ? "" : $"\n{result.Details}")}");

            if (!ContinueOnError)
                return new ToolCallOutcome(null,
                    result.ErrorCode ?? NodeErrorCodes.CommonError,
                    result.ErrorMessage ?? $"Tool '{toolCall.name}' failed.");

            return new ToolCallOutcome(
                $"[Tool error] {result.ErrorCode ?? NodeErrorCodes.CommonError}: {result.ErrorMessage}", null, null);
        }
        private string ResolveSender() => EmitSender switch
        {
            PostToolResultSender.User => Sender.User,
            PostToolResultSender.System => Sender.System,
            _ => Sender.ToolResult
        };

        private PostNodeResult Fail(string code, string message)
        {
            var result = PostNodeResult.Failure(code, message);
            result.NodeId = Id;
            result.NodeName = Name;
            return result;
        }
    }
}
