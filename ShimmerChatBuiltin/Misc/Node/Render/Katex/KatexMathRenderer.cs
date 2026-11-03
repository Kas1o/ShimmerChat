using System.Collections.Concurrent;
using KatexEngine = KStyleMath.Katex;
using KatexSettings = KStyleMath.Settings;

namespace ShimmerChatBuiltin.Misc.Node.Render.Katex
{
    /// <summary>
    /// 数学公式输出形态。
    /// </summary>
    public enum MathOutputMode
    {
        /// <summary>仅输出 MathML，由浏览器原生渲染，不需要任何额外样式文件。</summary>
        MathML,

        /// <summary>
        /// 输出与 Katex.renderToString 一致的 HTML + MathML 标记。
        /// 需要 katex.min.css（含字体）才能正确显示。
        /// </summary>
        KatexHtml
    }

    /// <summary>
    /// 基于 KStyleMath（KaTeX 0.19.1 的纯 C# 移植）的公式渲染器。
    /// LaTeX 解析失败时按 KaTeX 的约定输出 <c>katex-error</c> 标记（红色错误文本），不做静默降级。
    /// </summary>
    public sealed class KatexMathRenderer
    {
        /// <summary>渲染结果缓存上限。公式在消息重渲染时会被反复请求，缓存可避免重复解析。</summary>
        private const int CacheLimit = 2048;

        private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

        private readonly string _output;

        public MathOutputMode OutputMode { get; }

        public KatexMathRenderer(MathOutputMode outputMode)
        {
            OutputMode = outputMode;
            _output = outputMode switch
            {
                MathOutputMode.MathML => "mathml",
                MathOutputMode.KatexHtml => "htmlAndMathml",
                _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, "未知的公式输出形态")
            };
        }

        /// <summary>
        /// 渲染单个公式。<paramref name="latex"/> 不含定界符。
        /// </summary>
        public string Render(string latex, bool displayMode)
        {
            var key = string.Concat(
                displayMode ? "D" : "I", "\u0000",
                _output, "\u0000",
                latex);

            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var html = KatexEngine.RenderToString(latex, new KatexSettings
            {
                DisplayMode = displayMode,
                Output = _output,
                // 解析失败时交由 KStyleMath 产出 katex-error 标记，错误对用户可见
                ThrowOnError = false
            });

            if (Cache.Count >= CacheLimit)
                Cache.Clear();
            Cache[key] = html;
            return html;
        }
    }
}
