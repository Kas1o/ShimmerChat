using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    /// <summary>
    /// 从 SharedState["ChatMessages"] 读取对话消息列表，追加到 TransientEnv.Fragments。
    /// 替代 GenerationManagerV2 中硬编码的 AppendChatHistory，让消息注入逻辑可配置。
    /// </summary>
    [NodeInfo("node.append_chat_messages", Icon = "💬", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.append_chat_messages.desc")]
    public class AppendChatMessagesNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Append Chat Messages";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var sharedState = context.Env.Transient.SharedState;
            if (!sharedState.TryGetValue("ChatMessages", out var obj) || obj is not List<Message> messages)
                return Task.FromResult(NodeResult.SuccessResult());

            var fragments = context.Env.Transient.Fragments;

            foreach (var msg in messages)
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
