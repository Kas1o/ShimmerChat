namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 一段 &lt;pre&gt;&lt;code&gt; 在 HTML 里的位置。
    /// 索引均为字符下标，区间一律左闭右开。
    /// </summary>
    internal readonly record struct CodeBlockSpan(
        int PreStart,
        int PreOpenEnd,
        int PreEnd,
        int CodeStart,
        int CodeOpenEnd,
        int CodeCloseStart,
        string Language)
    {
        /// <summary>&lt;code&gt; 与 &lt;/code&gt; 之间文本的起点。</summary>
        public int ContentStart => CodeOpenEnd + 1;

        /// <summary>&lt;code&gt; 与 &lt;/code&gt; 之间文本的长度。</summary>
        public int ContentLength => CodeCloseStart - ContentStart;

        /// <summary>&lt;pre ...&gt; 开标签（含尖括号）。</summary>
        public string PreOpenTag(string html) => html.Substring(PreStart, PreOpenEnd - PreStart + 1);

        /// <summary>该块是否已被 CodeBlockActionsNode 处理过（幂等标记）。</summary>
        public bool HasActionsMarker(string html) =>
            PreOpenTag(html).Contains(CodeBlockHtmlScanner.ActionsMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 在 HTML 里定位 &lt;pre&gt;&lt;code&gt; 块。
    ///
    /// 这里刻意不做通用 HTML 解析：输入来自 Markdig，代码正文里的 '&lt;' 已被转义成
    /// &amp;lt;，所以 &lt;/code&gt; / &lt;/pre&gt; 不可能出现在代码内容中，扫描结果是确定的。
    /// 换成 AngleSharp 之类的解析器会把整篇 HTML 重新序列化，既会让 ChangeLog 的 diff
    /// 失真，也可能破坏下游按字符串匹配的节点。
    /// </summary>
    internal static class CodeBlockHtmlScanner
    {
        /// <summary>写在 &lt;pre&gt; 开标签上的幂等标记。</summary>
        public const string ActionsMarker = "data-code-block";

        private const string PreTag = "<pre";
        private const string CodeTag = "<code";
        private const string PreCloseTag = "</pre>";
        private const string CodeCloseTag = "</code>";

        /// <summary>从 <paramref name="start"/> 起找下一个含 &lt;code&gt; 的代码块。</summary>
        public static bool TryFind(string html, int start, out CodeBlockSpan span)
        {
            var search = start;
            while (true)
            {
                span = default;

                var preStart = IndexOfOpeningTag(html, PreTag, search);
                if (preStart < 0) return false;

                var preOpenEnd = html.IndexOf('>', preStart);
                if (preOpenEnd < 0) return false;

                var preClose = html.IndexOf(PreCloseTag, preOpenEnd, StringComparison.OrdinalIgnoreCase);
                if (preClose < 0) return false;

                // 无论这个块是否可用，下一轮都从这里之后继续，避免原地重扫
                var next = preClose + PreCloseTag.Length;

                var codeStart = html.IndexOf(CodeTag, preOpenEnd, preClose - preOpenEnd,
                    StringComparison.OrdinalIgnoreCase);
                if (codeStart < 0 || !IsTagBoundary(html, codeStart + CodeTag.Length))
                {
                    search = next;
                    continue;
                }

                var codeOpenEnd = html.IndexOf('>', codeStart);
                if (codeOpenEnd < 0 || codeOpenEnd >= preClose)
                {
                    search = next;
                    continue;
                }

                var codeClose = html.IndexOf(CodeCloseTag, codeOpenEnd, preClose - codeOpenEnd,
                    StringComparison.OrdinalIgnoreCase);
                if (codeClose < 0)
                {
                    search = next;
                    continue;
                }

                var openTag = html.Substring(codeStart, codeOpenEnd - codeStart + 1);
                span = new CodeBlockSpan(preStart, preOpenEnd, next, codeStart, codeOpenEnd, codeClose,
                    NormalizeLanguage(ExtractLanguage(openTag)));
                return true;
            }
        }

        /// <summary>找 &lt;tag，后面必须是空白、'&gt;' 或 '/'，避免误匹配 &lt;prefix / &lt;preview。</summary>
        public static int IndexOfOpeningTag(string html, string tag, int start)
        {
            var index = start;
            while (true)
            {
                var found = html.IndexOf(tag, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return -1;

                var next = found + tag.Length;
                if (!IsTagBoundary(html, next))
                {
                    index = next;
                    continue;
                }

                return found;
            }
        }

        /// <summary>从 &lt;code ...&gt; 开标签的 class 里取 language-xxx / lang-xxx。</summary>
        public static string ExtractLanguage(string openTag)
        {
            var classIndex = openTag.IndexOf("class", StringComparison.OrdinalIgnoreCase);
            if (classIndex < 0) return string.Empty;

            var equals = openTag.IndexOf('=', classIndex);
            if (equals < 0) return string.Empty;

            var i = equals + 1;
            while (i < openTag.Length && char.IsWhiteSpace(openTag[i])) i++;
            if (i >= openTag.Length) return string.Empty;

            var quote = openTag[i];
            if (quote != '"' && quote != '\'') return string.Empty;

            var end = openTag.IndexOf(quote, i + 1);
            if (end < 0) return string.Empty;

            foreach (var token in openTag.Substring(i + 1, end - i - 1)
                         .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
                    return token["language-".Length..];
                if (token.StartsWith("lang-", StringComparison.OrdinalIgnoreCase))
                    return token["lang-".Length..];
            }

            return string.Empty;
        }

        /// <summary>解析“跳过着色的语言”配置：逗号 / 分号 / 空白分隔，大小写不敏感。</summary>
        public static HashSet<string> ParseLanguageSet(string? value)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value)) return set;

            foreach (var token in value.Split([',', ';', ' ', '\t', '\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                set.Add(token);
            }

            return set;
        }

        public static string NormalizeLanguage(string? value) => value?.Trim().Trim('"', '\'') ?? string.Empty;

        private static bool IsTagBoundary(string html, int index) =>
            index >= html.Length || char.IsWhiteSpace(html[index]) || html[index] == '>' || html[index] == '/';
    }
}
