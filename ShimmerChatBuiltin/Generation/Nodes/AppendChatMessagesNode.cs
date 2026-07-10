using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 从 SharedState["ChatMessages"] 读取对话消息列表，追加到 TransientEnv.Fragments。
    /// 替代 GenerationManagerV2 中硬编码的 AppendChatHistory，让消息注入逻辑可配置。
    /// </summary>
    [NodeInfo("Append Chat Messages", Icon = "💬", Color = "#60b0e0", Category = "Content/Fragment", Description = "Append chat history messages from SharedState into context fragments")]
    public class AppendChatMessagesNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Append Chat Messages";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
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
                    "user" => PromptBuilder.From.user,
                    "system" => PromptBuilder.From.system,
                    "ai" => PromptBuilder.From.assistant,
                    "toolresult" => PromptBuilder.From.tool_result,
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
