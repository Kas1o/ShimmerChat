using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// Agent 树的根节点 / 边界标记。Agent 持有的私有修改器树以此节点为根。
    /// 它负责将 Description 组装为初始 system 片段。
    /// </summary>
    [NodeInfo("Agent Root", Icon = "🏠", Color = "#4a9eff", Category = "Flow")]
    public class AgentRootNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Agent Root";

        /// <summary>
        /// Root 下的子节点
        /// </summary>
        public List<IGenerationNode> Nodes { get; set; } = new();

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            foreach (var node in Nodes)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var result = await node.ExecuteAsync(context);
                if (!result.Success)
                {
                    // 传播子节点的失败结果，并附上当前节点的信息
                    result.NodeId ??= Id;
                    result.NodeName ??= Name;
                    return result;
                }
            }
            return NodeResult.SuccessResult();
        }
    }
}
