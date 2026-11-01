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
                context.Env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: {toolCalls.Count - calls.Count} tool call(s) dropped by MaxCallsPerRound={MaxCallsPerRound}.");
            }

            foreach (var toolCall in calls)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var tool = env.Tools.FirstOrDefault(t => t.GetDefinition().name == toolCall.name);
                var result = await ExecuteOneAsync(env, tool, toolCall, context.CancellationToken);
                if (result == null)
                    return Fail(NodeErrorCodes.ToolNotFound,
                        $"Tool '{toolCall.name}' is not available in the current round.");

                PostChat.Append(env,
                    new ChatMessage { Content = result, id = toolCall.id },
                    ResolveSender());
            }

            // 本轮产生了工具结果，模型需要看到它们才能继续（或收尾）
            env.LoopDirective = PostLoopDirective.Continue;
            return PostNodeResult.SuccessResult();
        }

        /// <summary>返回 null 表示工具缺失且不允许继续。</summary>
        private async Task<string?> ExecuteOneAsync(PostGenerationEnv env, IToolV2? tool,
            ToolCall toolCall, CancellationToken ct)
        {
            if (tool == null)
            {
                if (!ContinueOnError)
                    return null;

                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: tool '{toolCall.name}' not found, reporting to the model.");
                return $"[Tool error] Tool '{toolCall.name}' not found.";
            }

            try
            {
                return await tool.ExecuteAsync(toolCall.arguments ?? "{}");
            }
            catch (Exception ex) when (ContinueOnError)
            {
                // 吞异常必须留痕：模型看到的是错误文本，排查依赖 DebugOutput
                env.Persistent.DebugOutput.Write("PostToolCallNode", "ToolCall",
                    $"Round {env.RoundIndex}: tool '{toolCall.name}' threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return $"[Tool error] {ex.Message}";
            }
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
