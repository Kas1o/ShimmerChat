using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.render_sequence",
        Icon = "▶",
        Color = "var(--node-sequence)",
        CategoryKeys = ["category.flow"],
        DescriptionKey = "node.render_sequence.desc")]
    public class RenderSequenceNode : IRenderModifierNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        [NodeProperty("prop.node.name", Order = -100)]
        public string Name { get; set; } = "Sequence";

        [NodeProperty("prop.sequence.children", Order = 100)]
        public List<IRenderModifierNode> Children { get; set; } = new();

        public async Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context)
        {
            foreach (var child in Children)
            {
                var result = await child.ExecuteAsync(context);
                if (!result.Success)
                    return result;
                context.Env.UpdateContent(result.Content, child.Name, child.GetType().Name);
            }
            return RenderNodeResult.SuccessResult(context.Env.GetContent());
        }
    }
}
