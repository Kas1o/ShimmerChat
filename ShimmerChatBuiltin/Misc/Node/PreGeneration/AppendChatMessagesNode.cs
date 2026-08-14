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
    [NodeInfo("node.append_chat_messages", Icon = "💬", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.append_chat_messages.desc")]
    public class AppendChatMessagesNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Append Chat Messages";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
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

                fragments.Add(new ContextSegment
                {
                    Message = msg.message,
                    From = from,
                    Metadata = new Dictionary<string, object>
                    {
                        ["timestamp"] = msg.timestamp,
                        ["sender"] = msg.sender
                    }
                });
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
