using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ShimmerChatBuiltin.Misc.Node.Render.Katex
{
    /// <summary>
    /// 把 <c>$...$</c>、<c>$$...$$</c>、<c>\(...\)</c>、<c>\[...\]</c> 渲染为公式标记的 Markdig 内联解析器。
    /// 代码块与代码段由 Markdig 自己的解析器先接管，因此其中的 <c>$</c> 不受影响；
    /// <c>\$</c> 由转义解析器处理，同样不会被当成定界符。
    /// </summary>
    public sealed class KatexInlineParser : InlineParser
    {
        private readonly KatexMathRenderer _renderer;

        public KatexInlineParser(KatexMathRenderer renderer)
        {
            _renderer = renderer;
            OpeningCharacters = ['$', '\\'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var text = slice.Text;
            var start = slice.Start;
            var end = slice.End;

            string closer;
            bool display;
            int contentStart;

            if (text[start] == '\\')
            {
                if (start >= end) return false;
                switch (text[start + 1])
                {
                    case '(':
                        closer = "\\)";
                        display = false;
                        break;
                    case '[':
                        closer = "\\]";
                        display = true;
                        break;
                    default:
                        // 其余反斜杠序列留给 Markdig 的转义解析器
                        return false;
                }
                contentStart = start + 2;
            }
            else
            {
                display = start < end && text[start + 1] == '$';
                closer = display ? "$$" : "$";
                contentStart = start + closer.Length;
            }

            if (contentStart > end) return false;

            var closeIndex = FindCloser(text, contentStart, end, closer);
            if (closeIndex < 0) return false;

            var latex = text.Substring(contentStart, closeIndex - contentStart);
            if (!IsRenderable(latex, display)) return false;

            processor.Inline = new HtmlInline(_renderer.Render(latex, display))
            {
                Span = new SourceSpan(
                    processor.GetSourcePosition(start),
                    processor.GetSourcePosition(closeIndex + closer.Length - 1))
            };

            slice.Start = closeIndex + closer.Length;
            return true;
        }

        /// <summary>从 <paramref name="from"/> 开始查找结束定界符，被反斜杠转义的字符会被跳过。</summary>
        private static int FindCloser(string text, int from, int end, string closer)
        {
            for (var i = from; i <= end; i++)
            {
                var c = text[i];

                // 先判定结束定界符：\( 与 \[ 的定界符本身就是反斜杠序列
                if (c == closer[0] && (closer.Length == 1 || (i + 1 <= end && text[i + 1] == closer[1])))
                    return i;

                // 跳过被反斜杠转义的字符，使 \$ 之类不会提前结束公式
                if (c == '\\') i++;
            }

            return -1;
        }

        /// <summary>
        /// 判定定界符之间的内容是否值得渲染。行内公式遵循 KaTeX auto-render 的约定：
        /// 内容首尾不能是空白（避免把 “$5 and $10” 这类金额当成公式）。
        /// </summary>
        private static bool IsRenderable(string latex, bool display)
        {
            if (string.IsNullOrWhiteSpace(latex)) return false;
            if (display) return true;

            if (char.IsWhiteSpace(latex[0]) || char.IsWhiteSpace(latex[^1])) return false;
            // 结尾是未转义的反斜杠说明这不是一个完整的公式
            if (latex[^1] == '\\') return false;

            return true;
        }
    }
}
