using ShimmerChatLib.Generation;
using System.Text.RegularExpressions;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.st_style_macro", Icon = "🔤", Color = "#60c080", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.st_style_macro.desc")]
    public class STStyleMacroNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "ST Style Macro";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var persistent = context.Env.Persistent;
            var username = persistent.KVData.Read("User", "username") ?? "User";
            var agent = persistent.GetAgent();
            var charname = agent.Name ?? agent.Guid.ToString();

            foreach (var segment in context.Env.Transient.Fragments)
            {
                segment.Message.Content = Regex.Replace(
                    segment.Message.Content, @"\{\{user\}\}", username, RegexOptions.IgnoreCase);
                segment.Message.Content = Regex.Replace(
                    segment.Message.Content, @"\{\{char\}\}", charname, RegexOptions.IgnoreCase);
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
