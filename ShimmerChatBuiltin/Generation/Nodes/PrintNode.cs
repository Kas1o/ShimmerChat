using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.print", Icon = "🖨️", Color = "#888", CategoryKeys = ["category.debug"], DescriptionKey = "node.print.desc")]
    public class PrintNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Print";

        [NodeProperty("prop.print.template", HintKey = "prop.print.template.hint")]
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
