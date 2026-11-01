using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    /// <summary>
    /// 显式设置本轮的循环指令。整棵后处理树执行完毕后由宿主统一应用，
    /// 因此把它放在树的末尾即可获得"最后决定"的语义。
    /// </summary>
    [NodeInfo("node.post_loop_directive",
        Icon = "↻",
        Color = "var(--node-branch)",
        CategoryKeys = ["category.post", "category.flow"],
        DescriptionKey = "node.post_loop_directive.desc")]
    public class PostLoopDirectiveNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Loop Control";

        [NodeProperty("prop.post_loop_directive.directive",
            HintKey = "prop.post_loop_directive.directive.hint")]
        public PostLoopDirective Directive { get; set; } = PostLoopDirective.Stop;

        public Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            context.Env.LoopDirective = Directive;
            return Task.FromResult(PostNodeResult.SuccessResult());
        }
    }
}
