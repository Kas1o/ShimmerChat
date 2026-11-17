using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    /// <summary>
    /// 顺序执行子节点，Repeat=N 表示重复执行 N 次（默认 1 次）
    /// </summary>
    [NodeInfo("node.sequence", Icon = "▦", Color = "var(--node-flow)", CategoryKeys = ["category.flow"])]
    public class SequenceNode : IPreGenerationNode
    {
        [NodeProperty("prop.sequence.nodes")]
        public List<PreGenerationNodeWrapper> Nodes { get; set; } = new();
        [NodeProperty("prop.sequence.repeat", HintKey = "prop.sequence.repeat.hint")]
        public int Repeat { get; set; } = 1;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            for (int r = 0; r < Repeat; r++)
            {
                foreach (var node in Nodes)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var result = await node.ExecuteAsync(context);
                    if (!result.Success)
                        return result;
                }
            }
            return NodeResult.SuccessResult();
        }
    }
}
