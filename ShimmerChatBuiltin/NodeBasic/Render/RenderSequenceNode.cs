using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render
{
    [NodeInfo("node.render_sequence",
        Icon = "▶",
        Color = "var(--node-sequence)",
        CategoryKeys = ["category.flow"],
        DescriptionKey = "node.render_sequence.desc")]
    public class RenderSequenceNode : IRenderModifierNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Sequence";

        [NodeProperty("prop.sequence.children", Order = 100)]
        public List<IRenderModifierNode> Children { get; set; } = new();

        public void Execute(RenderNodeExecutionContext context)
        {
            foreach (var child in Children)
            {
                child.Execute(context);
            }
        }
    }
}
