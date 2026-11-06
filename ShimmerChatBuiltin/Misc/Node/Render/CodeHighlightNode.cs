using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Meziantou.Framework.SyntaxHighlighting;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 给 HTML 里的围栏代码块加语法高亮。
    ///
    /// 必须排在 Markdown 渲染节点之后：输入是 HTML，节点只替换 &lt;code&gt; 与 &lt;/code&gt;
    /// 之间的文本，其余字节（含 &lt;pre&gt; / &lt;code&gt; 开标签）原样保留，避免影响下游按字符串
    /// 匹配的节点（如 RegexReplaceNode）。没有代码块时不做任何修改，也不产生 ChangeLog。
    /// </summary>
    [NodeInfo("node.code_highlight",
        Icon = "🎨",
        Color = "var(--node-config)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.code_highlight.desc")]
    public class CodeHighlightNode : IRenderModifierNode
    {
        /// <summary>高亮结果缓存上限（流式重渲染时同一代码块会被反复请求）。</summary>
        private const int CacheLimit = 512;

        private static readonly ConcurrentDictionary<CacheKey, string> Cache = new();

        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Code Highlight";

        /// <summary>
        /// 不参与着色的语言（逗号 / 分号 / 空白分隔，大小写不敏感）。
        /// 这些代码块会被原样保留，交给后续节点（如 SVG / Mermaid 直出）处理。
        /// </summary>
        [NodeProperty("prop.code_highlight.skip_languages",
            HintKey = "prop.code_highlight.skip_languages.hint", Order = 10)]
        public string SkipLanguages { get; set; } = "svg,mermaid";

        /// <summary>单个代码块超过此字符数就不着色（0 = 不限制）。</summary>
        [NodeProperty("prop.code_highlight.max_chars",
            HintKey = "prop.code_highlight.max_chars.hint", Order = 20)]
        public int MaxBlockChars { get; set; } = 20_000;

        /// <summary>单次正则匹配超时（毫秒，0 = 不启用）。AI 输出属于不可信输入。</summary>
        [NodeProperty("prop.code_highlight.match_timeout",
            HintKey = "prop.code_highlight.match_timeout.hint", Order = 30)]
        public int MatchTimeoutMs { get; set; } = 1000;

        /// <summary>没有语言标识的代码块按此语言着色；空 = 不着色。</summary>
        [NodeProperty("prop.code_highlight.default_language",
            HintKey = "prop.code_highlight.default_language.hint", Order = 40)]
        public string DefaultLanguage { get; set; } = "";

        public void Execute(RenderNodeExecutionContext context)
        {
            var html = context.Env.GetContent();
            if (string.IsNullOrEmpty(html) ||
                html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var result = HighlightBlocks(html);
            if (result is null)
                return;

            context.Env.UpdateContent(result, Name, GetType().Name);
        }

        /// <summary>遍历所有代码块并替换其中的代码文本；没有任何块被修改时返回 null。</summary>
        private string? HighlightBlocks(string html)
        {
            var skipped = CodeBlockHtmlScanner.ParseLanguageSet(SkipLanguages);
            var options = new HighlightOptions
            {
                MatchTimeout = MatchTimeoutMs > 0
                    ? TimeSpan.FromMilliseconds(MatchTimeoutMs)
                    : Timeout.InfiniteTimeSpan
            };

            StringBuilder? builder = null;
            var copied = 0;
            var search = 0;

            while (CodeBlockHtmlScanner.TryFind(html, search, out var block))
            {
                search = block.PreEnd;

                var language = block.Language;
                if (language.Length == 0)
                    language = CodeBlockHtmlScanner.NormalizeLanguage(DefaultLanguage);
                if (language.Length == 0 || skipped.Contains(language))
                    continue;

                if (MaxBlockChars > 0 && block.ContentLength > MaxBlockChars)
                    continue;

                var inner = html.Substring(block.ContentStart, block.ContentLength);
                // 已经带 hljs- class 说明这个块被处理过了（节点被加了两次），保持幂等
                if (inner.Contains("class=\"hljs-", StringComparison.Ordinal))
                    continue;

                var highlighted = HighlightCached(WebUtility.HtmlDecode(inner), language, options);
                if (highlighted is null)
                    continue;

                builder ??= new StringBuilder(html.Length + 1024);
                builder.Append(html, copied, block.ContentStart - copied);
                builder.Append(highlighted);
                copied = block.ContentStart + block.ContentLength;
            }

            if (builder is null)
                return null;

            builder.Append(html, copied, html.Length - copied);
            return builder.ToString();
        }

        private string? HighlightCached(string code, string language, HighlightOptions options)
        {
            var key = new CacheKey(language, code, options.MatchTimeout == Timeout.InfiniteTimeSpan
                ? 0
                : (int)options.MatchTimeout.TotalMilliseconds);

            if (Cache.TryGetValue(key, out var cached))
                return cached;

            if (!SyntaxHighlighter.TryHighlight(code, language, out var highlighted, options))
                return null;

            if (Cache.Count >= CacheLimit)
                Cache.Clear();

            Cache[key] = highlighted;
            return highlighted;
        }

        private readonly record struct CacheKey(string Language, string Code, int TimeoutMs);
    }
}
