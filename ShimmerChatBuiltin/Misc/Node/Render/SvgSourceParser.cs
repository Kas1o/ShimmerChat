using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ShimmerChatBuiltin.Misc.Node.Render
{
    /// <summary>
    /// 一段可渲染的 SVG 源码。<see cref="Source"/> 保留用户原始文本（复制 / 下载用），
    /// <see cref="Base64"/> 则是补全默认命名空间后的 UTF-8 序列化结果（data URL 用）。
    /// </summary>
    internal sealed record SvgSource(string Source, string Base64, string? Title);

    /// <summary>
    /// 把代码块文本识别成 SVG。
    ///
    /// 只接受 XML 根节点为 &lt;svg&gt; 的文档，并且由宿主用 XmlReader 以禁用 DTD 解析、
    /// 不解析外部资源的方式读取。AI 输出属于不可信输入，不能把原始字符串直接拼进 HTML。
    /// </summary>
    internal static class SvgSourceParser
    {
        private const string SvgNamespace = "http://www.w3.org/2000/svg";

        public static bool TryPrepare(string? raw, int maxChars, out SvgSource result)
        {
            result = null!;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            // Markdig 会保留代码块首尾换行；BOM 只可能出现在真正粘贴进来的文本开头。
            var source = raw.Trim().Trim('\uFEFF');
            if (source.Length == 0)
                return false;

            if (maxChars > 0 && source.Length > maxChars)
                return false;

            try
            {
                var settings = new XmlReaderSettings
                {
                    // 不解析 DTD：既能阻止外部实体，也能避免实体膨胀这类 XML 炸弹。
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersInDocument = source.Length,
                    MaxCharactersFromEntities = 1_024,
                    CloseInput = true
                };

                using var reader = XmlReader.Create(new StringReader(source), settings);
                var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                var root = document.Root;

                if (root is null || root.Name.LocalName != "svg")
                    return false;

                // 没有命名空间的裸 <svg> 在浏览器以 image/svg+xml 解析时可能不显示；
                // 其他命名空间则明确不是 SVG，不能误判。
                if (root.Name.NamespaceName.Length != 0 &&
                    root.Name.NamespaceName != SvgNamespace)
                    return false;

                var renderDocument = document;
                if (root.Name.NamespaceName.Length == 0)
                {
                    // LINQ to XML 不允许给“本身不在命名空间里”的元素补一个默认 xmlns：
                    // 序列化时会报 prefix '' cannot be redefined。必须重建元素树。
                    var renamedRoot = ReNamespace(root, SvgNamespace);
                    renderDocument = new XDocument(renamedRoot);
                }

                // data URL 的字节固定为 UTF-8；把原始声明里的 encoding 改掉，
                // 避免下载后按错误的编码解析，同时让无声明源码下载后也能直接打开。
                var declaredStandalone = document.Declaration?.Standalone;
                renderDocument.Declaration = new XDeclaration("1.0", "UTF-8", declaredStandalone);
                var renderXml = renderDocument.ToString(SaveOptions.DisableFormatting);
                var title = root.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "title")?
                    .Value.Trim();
                if (string.IsNullOrEmpty(title))
                    title = null;

                result = new SvgSource(
                    source,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(renderXml)),
                    title);
                return true;
            }
            catch (XmlException)
            {
                // 非法 XML / 非 SVG 代码块不是渲染错误，保留原代码块即可。
                return false;
            }
        }

        /// <summary>
        /// 把没有命名空间的 SVG 元素树重建到 SVG 默认命名空间。
        /// 已经显式属于其他命名空间的子树（如 foreignObject 里的 XHTML）保持原样。
        /// </summary>
        private static XElement ReNamespace(XElement source, XNamespace svgNamespace)
        {
            var elementNamespace = source.Name.NamespaceName.Length == 0
                ? svgNamespace
                : source.Name.Namespace;
            var result = new XElement(elementNamespace + source.Name.LocalName);

            foreach (var attribute in source.Attributes())
            {
                // 默认命名空间由重建后的元素自身提供；保留 xlink / xml 等前缀声明。
                if (attribute.IsNamespaceDeclaration &&
                    attribute.Name.LocalName == "xmlns")
                    continue;

                result.Add(new XAttribute(attribute.Name, attribute.Value));
            }

            foreach (var node in source.Nodes())
            {
                switch (node)
                {
                    case XElement child:
                        result.Add(ReNamespace(child, svgNamespace));
                        break;
                    case XCData cdata:
                        result.Add(new XCData(cdata.Value));
                        break;
                    case XText text:
                        result.Add(new XText(text.Value));
                        break;
                    case XComment comment:
                        result.Add(new XComment(comment.Value));
                        break;
                    case XProcessingInstruction instruction:
                        result.Add(new XProcessingInstruction(
                            instruction.Target, instruction.Data));
                        break;
                }
            }

            return result;
        }
    }
}
