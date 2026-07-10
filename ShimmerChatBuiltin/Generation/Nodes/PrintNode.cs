using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("Print", Icon = "🖨️", Color = "#888", Category = "Debug", Description = "Print a template to console with {time} and {total_len} macro support")]
    public class PrintNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Print";

        [NodeProperty("Template", Hint = "Template string. Supports {time} and {total_len} macros")]
        public string Template { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;
            Console.WriteLine(Template
                .Replace("{time}", DateTime.Now.ToString("g"))
                .Replace("{total_len}", fragments.Select(s => s.Message.Content?.Length ?? 0).Sum().ToString()));
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
