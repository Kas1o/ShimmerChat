using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    /// <summary>
    /// 注释节点：多行自由编辑，不产生任何效果，仅用于在节点树中记录说明。
    /// </summary>
    [NodeInfo("node.comment", Icon = "✏️", Color = "var(--node-debug)", CategoryKeys = ["category.other"], DescriptionKey = "node.comment.desc")]
    public class CommentNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Comment";

        [NodeProperty("prop.comment.text", HintKey = "prop.comment.text.hint", MultiLine = true)]
        public string Text { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            // 注释节点不执行任何操作。
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
