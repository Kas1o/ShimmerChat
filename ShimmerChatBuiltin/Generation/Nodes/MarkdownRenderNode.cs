using Markdig;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 将 Markdown 文本渲染为 HTML。作为渲染管线中的节点，用户可配置 Markdig 扩展。
    /// </summary>
    [NodeInfo("node.markdown_render",
        Icon = "⬇",
        Color = "var(--node-api)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.markdown_render.desc")]
    public class MarkdownRenderNode : IRenderModifierNode
    {
        private static readonly MarkdownPipeline DefaultPipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .Build();

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Markdown Render";

        [NodeProperty("prop.markdown.pipe_tables", HintKey = "prop.markdown.pipe_tables_hint", Order = 10)]
        public bool EnablePipeTables { get; set; } = true;

        public Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context)
        {
            var content = context.Env.GetContent();
            if (string.IsNullOrEmpty(content))
                return Task.FromResult(RenderNodeResult.SuccessResult(content));

            var pipeline = EnablePipeTables
                ? DefaultPipeline
                : new MarkdownPipelineBuilder().Build();

            var html = Markdown.ToHtml(content, pipeline);
            return Task.FromResult(RenderNodeResult.SuccessResult(html));
        }
    }
}
