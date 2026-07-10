using SharperLLM.Util;
using ShimmerChatLib.Generation;
using System.Text;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    public enum MergeTargetRole
    {
        System,
        User,
        Assistant
    }

    [NodeInfo("Merge Fragments", Icon = "🔗", Color = "#c080e0", Category = "Content/Fragment", Description = "Merge all context fragments into a single fragment with the selected role")]
    public class MergeFragmentsNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Merge Fragments";

        [NodeProperty("Target Role", Hint = "Role assigned to the merged fragment")]
        public MergeTargetRole TargetRole { get; set; } = MergeTargetRole.System;

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;
            if (fragments.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            var sb = new StringBuilder();
            foreach (var seg in fragments)
            {
                sb.AppendLine(">>>" + seg.From.ToString() + ":");
                sb.AppendLine(seg.Message.Content);
            }

            var from = TargetRole switch
            {
                MergeTargetRole.System => PromptBuilder.From.system,
                MergeTargetRole.User => PromptBuilder.From.user,
                MergeTargetRole.Assistant => PromptBuilder.From.assistant,
                _ => PromptBuilder.From.system
            };

            fragments.Clear();
            fragments.Add(new ContextSegment
            {
                Message = new ChatMessage { Content = sb.ToString() },
                From = from
            });

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
