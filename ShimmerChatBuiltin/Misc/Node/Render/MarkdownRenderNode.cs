using System.Collections.Concurrent;
using Markdig;
using ShimmerChatBuiltin.Misc.Node.Render.Katex;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 将 Markdown 文本渲染为 HTML。作为渲染管线中的节点，用户可配置 Markdig 扩展与公式渲染。
    /// </summary>
    [NodeInfo("node.markdown_render",
        Icon = "⬇",
        Color = "var(--node-api)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.markdown_render.desc")]
    public class MarkdownRenderNode : IRenderModifierNode
    {
        private static readonly ConcurrentDictionary<PipelineKey, MarkdownPipeline> Pipelines = new();

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Markdown Render";

        [NodeProperty("prop.markdown.pipe_tables", HintKey = "prop.markdown.pipe_tables.hint", Order = 10)]
        public bool EnablePipeTables { get; set; } = true;

        [NodeProperty("prop.markdown.enable_math", HintKey = "prop.markdown.enable_math.hint", Order = 20)]
        public bool EnableMath { get; set; } = true;

        [NodeProperty("prop.markdown.math_output", HintKey = "prop.markdown.math_output.hint", Order = 30)]
        public MathOutputMode MathOutput { get; set; } = MathOutputMode.MathML;

        public void Execute(RenderNodeExecutionContext context)
        {
            var content = context.Env.GetContent();
            if (string.IsNullOrEmpty(content))
                return;

            var html = Markdown.ToHtml(content, GetPipeline());
            context.Env.UpdateContent(html, Name, GetType().Name);
        }

        private MarkdownPipeline GetPipeline()
        {
            var key = new PipelineKey(EnablePipeTables, EnableMath, MathOutput);
            return Pipelines.GetOrAdd(key, static k =>
            {
                var builder = new MarkdownPipelineBuilder();
                if (k.PipeTables)
                    builder.UsePipeTables();
                if (k.Math)
                    builder.UseKatexMath(new KatexMathRenderer(k.MathOutput));
                return builder.Build();
            });
        }

        private readonly record struct PipelineKey(bool PipeTables, bool Math, MathOutputMode MathOutput);
    }
}
