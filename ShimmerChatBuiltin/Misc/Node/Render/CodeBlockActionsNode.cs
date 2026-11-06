using System.Net;
using System.Text;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 给代码块加上语言标签与「复制 / 下载」按钮。
    ///
    /// 节点只产出结构（按钮带 data-code-action 属性），行为由宿主的
    /// wwwroot/js/code-block-actions.js 通过事件委托实现——Blazor 用 MarkupString
    /// 注入的 HTML 里没法挂 @onclick，委托到 document 也正好扛得住流式重渲染。
    ///
    /// 必须排在 Markdown 渲染之后；与代码高亮节点的先后顺序无关（一个改 &lt;pre&gt; 外层，
    /// 一个只改 &lt;code&gt; 内文）。
    /// </summary>
    [NodeInfo("node.code_block_actions",
        Icon = "🧰",
        Color = "var(--node-config)",
        CategoryKeys = ["category.render"],
        DescriptionKey = "node.code_block_actions.desc")]
    public class CodeBlockActionsNode : IRenderModifierNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Code Block Actions";

        [NodeProperty("prop.code_actions.show_language",
            HintKey = "prop.code_actions.show_language.hint", Order = 10)]
        public bool ShowLanguage { get; set; } = true;

        [NodeProperty("prop.code_actions.show_copy",
            HintKey = "prop.code_actions.show_copy.hint", Order = 20)]
        public bool ShowCopy { get; set; } = true;

        [NodeProperty("prop.code_actions.show_download",
            HintKey = "prop.code_actions.show_download.hint", Order = 30)]
        public bool ShowDownload { get; set; } = true;

        public void Execute(RenderNodeExecutionContext context)
        {
            var html = context.Env.GetContent();
            if (string.IsNullOrEmpty(html) ||
                html.IndexOf("<pre", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var result = AddToolbars(html, context.Env.Loc);
            if (result is null)
                return;

            context.Env.UpdateContent(result, Name, GetType().Name);
        }

        private string? AddToolbars(string html, ILocService? loc)
        {
            var copyLabel = Encode(loc?["code.actions.copy"] ?? "Copy");
            var downloadLabel = Encode(loc?["code.actions.download"] ?? "Download");
            var copiedLabel = Encode(loc?["code.actions.copied"] ?? "Copied");

            StringBuilder? builder = null;
            var copied = 0;
            var search = 0;

            while (CodeBlockHtmlScanner.TryFind(html, search, out var block))
            {
                search = block.PreEnd;

                // 已经加过工具条（节点被加了两次），不重复包裹
                if (block.HasActionsMarker(html))
                    continue;

                builder ??= new StringBuilder(html.Length + 1024);
                builder.Append(html, copied, block.PreStart - copied);
                builder.Append(BuildHeader(block.Language, copyLabel, downloadLabel, copiedLabel));

                // 保留 <pre> 的其余属性与内容，只插入幂等标记
                builder.Append("<pre data-code-block=\"1\"");
                builder.Append(html, block.PreStart + "<pre".Length,
                    block.PreEnd - block.PreStart - "<pre".Length);
                builder.Append("</div>");

                copied = block.PreEnd;
            }

            if (builder is null)
                return null;

            builder.Append(html, copied, html.Length - copied);
            return builder.ToString();
        }

        private string BuildHeader(string language, string copyLabel, string downloadLabel, string copiedLabel)
        {
            var sb = new StringBuilder(256);
            sb.Append("<div class=\"code-block\"><div class=\"code-block-head\">");

            if (ShowLanguage && language.Length > 0)
                sb.Append("<span class=\"code-block-lang\">").Append(Encode(language)).Append("</span>");
            else
                sb.Append("<span class=\"code-block-lang\"></span>");

            sb.Append("<span class=\"code-block-tools\">");

            if (ShowCopy)
            {
                sb.Append("<button type=\"button\" class=\"code-block-btn\" data-code-action=\"copy\"")
                    .Append(" data-code-copied=\"").Append(copiedLabel).Append("\">")
                    .Append("<i class=\"bi bi-clipboard\" aria-hidden=\"true\"></i>")
                    .Append("<span class=\"code-block-btn-text\">").Append(copyLabel).Append("</span>")
                    .Append("</button>");
            }

            if (ShowDownload)
            {
                sb.Append("<button type=\"button\" class=\"code-block-btn\" data-code-action=\"download\"")
                    .Append(" data-code-filename=\"").Append(Encode(BuildFileName(language))).Append("\">")
                    .Append("<i class=\"bi bi-download\" aria-hidden=\"true\"></i>")
                    .Append("<span class=\"code-block-btn-text\">").Append(downloadLabel).Append("</span>")
                    .Append("</button>");
            }

            sb.Append("</span></div>");
            return sb.ToString();
        }

        /// <summary>按语言选一个下载扩展名，未知语言用 .txt。</summary>
        private static string BuildFileName(string language)
        {
            var extension = Extensions.TryGetValue(language, out var ext) ? ext : ".txt";
            return "snippet" + extension;
        }

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = ".cs", ["cs"] = ".cs", ["c#"] = ".cs",
            ["javascript"] = ".js", ["js"] = ".js", ["jsx"] = ".jsx", ["mjs"] = ".mjs",
            ["typescript"] = ".ts", ["ts"] = ".ts", ["tsx"] = ".tsx",
            ["python"] = ".py", ["py"] = ".py",
            ["java"] = ".java", ["kotlin"] = ".kt", ["kt"] = ".kt",
            ["go"] = ".go", ["golang"] = ".go", ["rust"] = ".rs", ["rs"] = ".rs",
            ["c"] = ".c", ["cpp"] = ".cpp", ["c++"] = ".cpp", ["cxx"] = ".cpp",
            ["h"] = ".h", ["hpp"] = ".hpp",
            ["php"] = ".php", ["ruby"] = ".rb", ["rb"] = ".rb",
            ["swift"] = ".swift", ["scala"] = ".scala", ["dart"] = ".dart",
            ["html"] = ".html", ["xml"] = ".xml", ["svg"] = ".svg", ["vue"] = ".vue", ["svelte"] = ".svelte",
            ["css"] = ".css", ["scss"] = ".scss", ["less"] = ".less",
            ["json"] = ".json", ["jsonc"] = ".jsonc", ["yaml"] = ".yml", ["yml"] = ".yml",
            ["toml"] = ".toml", ["ini"] = ".ini", ["properties"] = ".properties",
            ["sql"] = ".sql", ["graphql"] = ".graphql", ["gql"] = ".graphql",
            ["bash"] = ".sh", ["sh"] = ".sh", ["shell"] = ".sh", ["zsh"] = ".sh",
            ["powershell"] = ".ps1", ["ps1"] = ".ps1", ["pwsh"] = ".ps1",
            ["bat"] = ".bat", ["cmd"] = ".bat", ["dos"] = ".bat",
            ["dockerfile"] = ".dockerfile", ["makefile"] = ".mk",
            ["markdown"] = ".md", ["md"] = ".md", ["mermaid"] = ".mmd",
            ["diff"] = ".diff", ["patch"] = ".patch",
            ["lua"] = ".lua", ["perl"] = ".pl", ["r"] = ".r",
            ["plaintext"] = ".txt", ["text"] = ".txt", ["txt"] = ".txt",
        };
    }
}
