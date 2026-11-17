using System.Net;
using System.Text;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 把围栏代码块里的 SVG 渲染成图片。
    ///
    /// 识别不依赖代码块语言：只要代码块内容是合法 XML 且根节点为 &lt;svg&gt;，就整体替换为
    /// &lt;figure&gt;。渲染使用 data URL + &lt;img&gt;，而不是把 SVG 直接内联进 HTML：
    /// 浏览器在 image 模式下不会执行 SVG 里的脚本，避免模型输出成为 XSS 载体。
    ///
    /// 必须排在 Markdown 渲染之后、代码高亮 / 代码块工具条之前。后者先执行会给代码块
    /// 套上工具条或注入高亮 span，导致内容不再是可直接解析的 XML。
    /// </summary>
    [NodeInfo("node.svg_render",
        Icon = "🖼️",
        Color = "var(--node-config)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.svg_render.desc")]
    public class SvgRenderNode : IRenderModifierNode
    {


        [NodeProperty("prop.svg_render.show_copy",
            HintKey = "prop.svg_render.show_copy.hint", Order = 10)]
        public bool ShowCopy { get; set; } = true;

        [NodeProperty("prop.svg_render.show_download",
            HintKey = "prop.svg_render.show_download.hint", Order = 20)]
        public bool ShowDownload { get; set; } = true;

        /// <summary>超过此字符数的代码块不做 SVG 解析（0 = 不限制）。</summary>
        [NodeProperty("prop.svg_render.max_chars",
            HintKey = "prop.svg_render.max_chars.hint", Order = 30)]
        public int MaxSvgChars { get; set; } = 200_000;

        public void Execute(RenderNodeExecutionContext context)
        {
            var html = context.Env.GetContent();
            if (string.IsNullOrEmpty(html) ||
                html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var result = ReplaceBlocks(html, context.Env.Loc);
            if (result is null)
                return;

            context.Env.UpdateContent(result, context.CurrentNodeName ?? GetType().Name, GetType().Name);
        }

        private string? ReplaceBlocks(string html, ILocService? loc)
        {
            var copyLabel = Localize(loc, "code.actions.copy", "Copy");
            var downloadLabel = Localize(loc, "code.actions.download", "Download");
            var copiedLabel = Localize(loc, "code.actions.copied", "Copied");
            var svgLabel = Localize(loc, "svg.render.label", "SVG");
            var altFallback = Localize(loc, "svg.render.alt", "SVG image");

            StringBuilder? builder = null;
            var copied = 0;
            var search = 0;

            while (CodeBlockHtmlScanner.TryFind(html, search, out var block))
            {
                // 无论是否命中都从 </pre> 之后继续，避免反复扫描同一个块。
                search = block.PreEnd;

                var source = WebUtility.HtmlDecode(
                    html.Substring(block.ContentStart, block.ContentLength));
                if (!SvgSourceParser.TryPrepare(source, MaxSvgChars, out var svg))
                    continue;

                builder ??= new StringBuilder(html.Length + 4096);
                builder.Append(html, copied, block.PreStart - copied);
                builder.Append(BuildFigure(svg, copyLabel, downloadLabel, copiedLabel,
                    svgLabel, altFallback));
                copied = block.PreEnd;
            }

            if (builder is null)
                return null;

            builder.Append(html, copied, html.Length - copied);
            return builder.ToString();
        }

        private string BuildFigure(SvgSource svg, string copyLabel, string downloadLabel,
            string copiedLabel, string svgLabel, string altFallback)
        {
            var sb = new StringBuilder(svg.Base64.Length + 1024);
            sb.Append("<figure class=\"svg-render\" data-svg-render=\"1\">");
            sb.Append("<div class=\"svg-render-stage\">");
            sb.Append("<img class=\"svg-render-image\" src=\"data:image/svg+xml;base64,")
                .Append(svg.Base64)
                .Append("\" alt=\"").Append(Encode(svg.Title ?? altFallback))
                .Append("\" decoding=\"async\" />");
            sb.Append("</div>");

            if (ShowCopy || ShowDownload)
            {
                sb.Append("<figcaption class=\"svg-render-bar\">");
                sb.Append("<span class=\"svg-render-label\">").Append(Encode(svgLabel))
                    .Append("</span>");
                sb.Append("<span class=\"svg-render-tools\">");

                if (ShowCopy)
                {
                    sb.Append("<button type=\"button\" class=\"svg-render-btn\"")
                        .Append(" data-svg-action=\"copy\" data-svg-copied=\"")
                        .Append(Encode(copiedLabel)).Append("\">")
                        .Append("<i class=\"bi bi-clipboard\" aria-hidden=\"true\"></i>")
                        .Append("<span class=\"svg-render-btn-text\">")
                        .Append(Encode(copyLabel)).Append("</span></button>");
                }

                if (ShowDownload)
                {
                    sb.Append("<button type=\"button\" class=\"svg-render-btn\"")
                        .Append(" data-svg-action=\"download\"")
                        .Append(" data-svg-filename=\"snippet.svg\">")
                        .Append("<i class=\"bi bi-download\" aria-hidden=\"true\"></i>")
                        .Append("<span class=\"svg-render-btn-text\">")
                        .Append(Encode(downloadLabel)).Append("</span></button>");
                }

                sb.Append("</span></figcaption>");
            }

            // template 的内容是惰性 DOM：把源码整体 HTML 编码后作为纯文本存放，
            // 供脚本复制 / 下载，同时不会参与页面渲染或执行。
            sb.Append("<template class=\"svg-render-source\">")
                .Append(Encode(svg.Source))
                .Append("</template>");
            sb.Append("</figure>");
            return sb.ToString();
        }

        private static string Localize(ILocService? loc, string key, string fallback)
        {
            var value = loc?[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
