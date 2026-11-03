using Markdig;
using Markdig.Renderers;

namespace ShimmerChatBuiltin.Misc.Node.Render.Katex
{
    /// <summary>
    /// 为 Markdig 管线挂上 <see cref="KatexInlineParser"/>。
    /// </summary>
    public sealed class KatexMarkdownExtension : IMarkdownExtension
    {
        private readonly KatexMathRenderer _renderer;

        public KatexMarkdownExtension(KatexMathRenderer renderer) => _renderer = renderer;

        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<KatexInlineParser>())
            {
                // 必须排在转义解析器之前，否则 \( 与 \[ 会先被当成转义字符吃掉
                pipeline.InlineParsers.Insert(0, new KatexInlineParser(_renderer));
            }
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
        }
    }

    public static class KatexMarkdownExtensions
    {
        /// <summary>启用公式渲染（KStyleMath）。</summary>
        public static MarkdownPipelineBuilder UseKatexMath(this MarkdownPipelineBuilder builder, KatexMathRenderer renderer)
        {
            builder.Extensions.Add(new KatexMarkdownExtension(renderer));
            return builder;
        }
    }
}
