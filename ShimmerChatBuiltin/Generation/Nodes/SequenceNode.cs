using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 顺序执行子节点，Repeat=N 表示重复执行 N 次（默认 1 次）
    /// </summary>
    [NodeInfo("node.sequence", Icon = "▦", Color = "#4a9eff", CategoryKeys = ["category.flow"])]
    public class SequenceNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Sequence";
        public List<IGenerationNode> Nodes { get; set; } = new();
        [NodeProperty("prop.sequence.repeat", HintKey = "prop.sequence.repeat.hint")]
        public int Repeat { get; set; } = 1;

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            for (int r = 0; r < Repeat; r++)
            {
                foreach (var node in Nodes)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var result = await node.ExecuteAsync(context);
                    if (!result.Success)
                    {
                        result.NodeId ??= Id;
                        result.NodeName ??= Name;
                        return result;
                    }
                }
            }
            return NodeResult.SuccessResult();
        }
    }
}
