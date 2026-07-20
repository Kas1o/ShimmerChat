using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    [NodeInfo("node.post_sequence",
        Icon = "▶",
        Color = "var(--node-sequence)",
        CategoryKeys = ["category.flow"],
        DescriptionKey = "node.post_sequence.desc")]
    public class PostSequenceNode : IPostGenerationNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "";

        [NodeProperty("prop.sequence.repeat", HintKey = "prop.sequence.repeat_hint", Order = 50)]
        public int Repeat { get; set; } = 1;

        [NodeProperty("prop.sequence.children", Order = 100)]
        public List<IPostGenerationNode> Children { get; set; } = new();

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            for (int r = 0; r < Repeat; r++)
            {
                if (context.CancellationToken.IsCancellationRequested)
                    return PostNodeResult.Failure("Cancelled", "Post sequence cancelled");

                foreach (var child in Children)
                {
                    var result = await child.ExecuteAsync(context);
                    if (!result.Success)
                        return result;
                }
            }
            return PostNodeResult.SuccessResult();
        }
    }
}
