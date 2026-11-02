using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    /// <summary>历史里存在未应答工具调用时的处理策略。</summary>
    public enum DanglingToolCallPolicy
    {
        /// <summary>为该调用注入一条占位 tool_result（只进 prompt，不改对话），并记录告警。</summary>
        InjectPlaceholder = 0,

        /// <summary>直接让生成失败，迫使调用方修复历史。</summary>
        FailFast = 1
    }

    /// <summary>
    /// 从 PersistentEnv.Chat.Messages 实时读取对话消息，追加到 TransientEnv.Fragments。
    /// 直接读取 Chat 对象而非快照副本，树执行期间对 chat.Messages 的修改立即可见。
    /// 替代 GenerationManagerV2 中硬编码的 AppendChatHistory，让消息注入逻辑可配置。
    /// </summary>
    /// <remarks>
    /// 这里也是图像附件的解析点：消息持久化的图像可能是 UserUploadImage 里的文件引用，
    /// 在此按需读成 Base64 内容块（SharperLLM 侧只处理 Base64），
    /// 避免把文件系统细节带进 LLM 连接库。解析发生在消息副本上，不会污染被持久化的消息。
    /// <para>
    /// 同时是<strong>工具调用完整性</strong>的唯一检查点：assistant 消息带 toolCalls 却没有配对的
    /// tool_result 时，OpenAI 兼容 API 会直接拒绝请求。历史里出现悬空调用的来源有：进程崩溃、
    /// 用户停止、工具被 MaxCallsPerRound 丢弃、工具结果被配置成 user/system 消息等。
    /// 默认注入占位并留痕，不做静默修补。
    /// </para>
    /// </remarks>
    [NodeInfo("node.append_chat_messages", Icon = "💬", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.append_chat_messages.desc")]
    public class AppendChatMessagesNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Append Chat Messages";

        [NodeProperty("prop.append_chat_messages.dangling_tool_calls",
            HintKey = "prop.append_chat_messages.dangling_tool_calls.hint", Order = 10)]
        public DanglingToolCallPolicy DanglingToolCalls { get; set; } = DanglingToolCallPolicy.InjectPlaceholder;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;
            var messages = context.Env.Persistent.Chat.Messages;

            var unanswered = ToolCallPairing.EnumerateUnanswered(messages);
            var danglingByMessage = unanswered
                .GroupBy(u => u.Assistant)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (unanswered.Count > 0 && DanglingToolCalls == DanglingToolCallPolicy.FailFast)
            {
                var list = string.Join(", ", unanswered.Select(u =>
                    $"'{u.Call.name}'{(string.IsNullOrEmpty(u.Call.id) ? "" : $" (id {u.Call.id})")}"));

                const string fallbackTemplate =
                    "Chat history contains {0} unanswered tool call(s): {1}. " +
                    "Repair the messages or switch this node to InjectPlaceholder.";
                var template = context.Env.Persistent.LocService["node_err.dangling_tool_calls"];
                if (string.IsNullOrEmpty(template) || template == "node_err.dangling_tool_calls")
                    template = fallbackTemplate;

                return NodeResult.Failure(
                    NodeErrorCodes.ParseError,
                    string.Format(template, unanswered.Count, list),
                    details: "AppendChatMessagesNode.DanglingToolCalls = FailFast.",
                    nodeId: Id, nodeName: Name);
            }

            int injected = 0;
            List<UnansweredToolCall>? carry = null;

            foreach (var msg in messages)
            {
                // 占位要跟在"该 assistant 的整段工具结果"之后，才能保持 tool_calls 与 tool 消息的原始顺序
                if (carry != null && msg.sender != ShimmerChatLib.Sender.ToolResult)
                {
                    injected += InjectPlaceholders(fragments, carry);
                    carry = null;
                }

                if (msg.GenerationState == MessageGenerationState.Regenerating)
                    continue;

                var from = msg.sender.ToLower() switch
                {
                    ShimmerChatLib.Sender.User => PromptBuilder.From.user,
                    ShimmerChatLib.Sender.System => PromptBuilder.From.system,
                    ShimmerChatLib.Sender.AI => PromptBuilder.From.assistant,
                    ShimmerChatLib.Sender.ToolResult => PromptBuilder.From.tool_result,
                    _ => PromptBuilder.From.system
                };

                var segmentMessage = msg.message;

                if (msg.Images is { Count: > 0 })
                {
                    var (images, failure) = await ResolveImagesAsync(context, msg.Images);
                    if (failure != null)
                        return failure;

                    // 解析结果只写在副本上，绝不写回被持久化的消息
                    segmentMessage = (ChatMessage)segmentMessage.Clone();
                    segmentMessage.Images = images;
                }

                fragments.Add(new ContextSegment
                {
                    Message = segmentMessage,
                    From = from,
                    Metadata = new Dictionary<string, object>
                    {
                        ["timestamp"] = msg.timestamp,
                        ["sender"] = msg.sender
                    }
                });

                if (danglingByMessage.TryGetValue(msg, out var dangling))
                    carry = dangling;
            }

            if (carry != null)
                injected += InjectPlaceholders(fragments, carry);

            if (unanswered.Count > 0)
            {
                context.Env.Persistent.DebugOutput.Write("AppendChatMessagesNode", "ToolCallIntegrity",
                    $"Chat {context.Env.Persistent.ChatGuid}: {unanswered.Count} unanswered tool call(s); " +
                    $"{injected} placeholder(s) injected into the prompt.");
            }

            if (injected < unanswered.Count)
            {
                // 没有 id 的调用无法构造合法的 tool_result，只能记录——这种情况历史本身就修不了
                context.Env.Persistent.DebugOutput.Write("AppendChatMessagesNode", "ToolCallIntegrity",
                    $"{unanswered.Count - injected} unanswered tool call(s) have no tool_call id and cannot be " +
                    "repaired; the request may be rejected by strict providers.");
            }

            return NodeResult.SuccessResult();
        }

        /// <summary>为未应答的调用注入占位 tool_result（只进 Fragments，不写回对话）。返回注入条数。</summary>
        private static int InjectPlaceholders(
            List<ContextSegment> fragments, List<UnansweredToolCall> dangling)
        {
            int injected = 0;

            foreach (var call in dangling)
            {
                if (string.IsNullOrEmpty(call.Call.id))
                    continue;

                fragments.Add(new ContextSegment
                {
                    Message = new ChatMessage
                    {
                        id = call.Call.id,
                        Content = new JObject
                        {
                            ["status"] = "no_result",
                            ["tool"] = call.Call.name,
                            ["tool_call_id"] = call.Call.id,
                            ["note"] = "This tool call was interrupted before producing a result. " +
                                       "Do not assume its output."
                        }.ToString(Formatting.None)
                    },
                    From = PromptBuilder.From.tool_result,
                    Metadata = new Dictionary<string, object>
                    {
                        ["tool_call_placeholder"] = true,
                        ["sender"] = ShimmerChatLib.Sender.ToolResult
                    }
                });

                injected++;
            }

            return injected;
        }

        /// <summary>
        /// 把消息的图像附件解析为请求内容块。缺服务、缺文件、附件数据非法都视为明确失败，
        /// 由管线向上汇报，绝不静默丢图让用户以为模型看到了图像。
        /// </summary>
        private async Task<(List<ChatImage>? Images, NodeResult? Failure)> ResolveImagesAsync(
            PreNodeExecutionContext context, IReadOnlyList<MessageImage> images)
        {
            var attachments = context.Env.Persistent.ImageAttachments;
            if (attachments == null)
            {
                return (null, NodeResult.Failure(
                    NodeErrorCodes.ServiceError,
                    "AppendChatMessages: 图像附件服务不可用，无法把消息中的图像解析为请求内容。",
                    details: "PersistentEnv.ImageAttachments is null.",
                    nodeId: Id, nodeName: Name));
            }

            var resolved = new List<ChatImage>(images.Count);
            foreach (var image in images)
            {
                try
                {
                    resolved.Add(await attachments.ToChatImageAsync(image, context.CancellationToken));
                }
                catch (Exception ex)
                {
                    return (null, NodeResult.Failure(
                        NodeErrorCodes.DataMissing,
                        $"AppendChatMessages: 无法加载消息中的图像（{image.FileName ?? image.MimeType}）：{ex.Message}",
                        details: ex.ToString(),
                        nodeId: Id, nodeName: Name));
                }
            }

            return (resolved, null);
        }
    }
}
