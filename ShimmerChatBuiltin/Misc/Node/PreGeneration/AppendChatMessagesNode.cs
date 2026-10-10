using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    /// <summary>
    /// 从 PersistentEnv.Chat.Messages 实时读取对话消息，追加到 TransientEnv.Fragments。
    /// 直接读取 Chat 对象而非快照副本，树执行期间对 chat.Messages 的修改立即可见。
    /// 替代 GenerationManagerV2 中硬编码的 AppendChatHistory，让消息注入逻辑可配置。
    /// </summary>
    /// <remarks>
    /// 这里也是图像附件的解析点：消息持久化的图像可能是 UserUploadImage 里的文件引用，
    /// 在此按需读成 Base64 内容块（SharperLLM 侧只处理 Base64），
    /// 避免把文件系统细节带进 LLM 连接库。解析发生在消息副本上，不会污染被持久化的消息。
    /// </remarks>
    [NodeInfo("node.append_chat_messages", Icon = "💬", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.append_chat_messages.desc")]
    public class AppendChatMessagesNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Append Chat Messages";

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;

            foreach (var msg in context.Env.Persistent.Chat.Messages)
            {
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
            }

            return NodeResult.SuccessResult();
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
