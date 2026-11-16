using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// SvgRenderNode：代码块识别、渲染结构、安全边界、开关与降级行为。
/// </summary>
public class SvgRenderNodeTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"4\" /></svg>";

    private static RenderEnv CreateEnv(string html, ILocService? loc = null) =>
        new(html, new Mock<ITreeNodeSerializer>().Object, new Mock<IKVDataService>().Object,
            chat: null, agent: null, loc: loc);

    private static string Run(string html, Action<SvgRenderNode>? configure = null,
        ILocService? loc = null)
    {
        var env = CreateEnv(html, loc);
        var node = new SvgRenderNode();
        configure?.Invoke(node);
        node.Execute(new RenderNodeExecutionContext(env));
        return env.GetContent();
    }

    private static string Fence(string language, string code) =>
        $"<pre><code class=\"language-{language}\">{WebUtility.HtmlEncode(code)}</code></pre>";

    private static string DecodeDataUrl(string html)
    {
        const string marker = "src=\"data:image/svg+xml;base64,";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += marker.Length;
        var end = html.IndexOf('"', start);
        end.Should().BeGreaterThan(start);
        return Encoding.UTF8.GetString(Convert.FromBase64String(html[start..end]));
    }

    private static string DecodeTemplate(string html)
    {
        const string open = "<template class=\"svg-render-source\">";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += open.Length;
        var end = html.IndexOf("</template>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return WebUtility.HtmlDecode(html[start..end]);
    }

    [Fact]
    public void SvgBlock_IsReplacedByRenderCard()
    {
        var result = Run(Fence("svg", Svg));

        result.Should().NotContain("<pre>");
        result.Should().Contain("<figure class=\"svg-render\" data-svg-render=\"1\">");
        result.Should().Contain("class=\"svg-render-image\"");
        result.Should().Contain("alt=\"SVG image\"");
        result.Should().Contain("data-svg-action=\"copy\"");
        result.Should().Contain("data-svg-action=\"download\"");
        result.Should().Contain("data-svg-filename=\"snippet.svg\"");
        result.Should().Contain("<template class=\"svg-render-source\">");
        result.Should().EndWith("</figure>");

        DecodeDataUrl(result).Should().Contain("<circle");
        DecodeTemplate(result).Should().Be(Svg);
    }

    [Fact]
    public void XMLWithSvgRoot_IsRendered()
    {
        var xml = "<?xml version=\"1.0\"?>\n" + Svg;

        var result = Run(Fence("xml", xml));

        result.Should().Contain("data-svg-render=\"1\"");
        DecodeTemplate(result).Should().Contain("<?xml version=\"1.0\"?>");
        DecodeDataUrl(result).Should().Contain("<svg");
    }

    [Fact]
    public void Detection_DoesNotDependOnFenceLanguage()
    {
        var result = Run(Fence("text", Svg));

        result.Should().Contain("data-svg-render=\"1\"");
        result.Should().NotContain("<pre>");
    }

    [Fact]
    public void NonSvgXml_IsLeftUntouched()
    {
        var html = Fence("xml", "<root><child /></root>");
        var env = CreateEnv(html);

        new SvgRenderNode().Execute(new RenderNodeExecutionContext(env));

        env.GetContent().Should().Be(html);
        env.ChangeLog.Should().BeEmpty();
    }

    [Fact]
    public void InvalidSvg_IsLeftUntouched()
    {
        var html = Fence("svg", "<svg><circle></svg>");
        var env = CreateEnv(html);

        new SvgRenderNode().Execute(new RenderNodeExecutionContext(env));

        env.GetContent().Should().Be(html);
        env.ChangeLog.Should().BeEmpty();
    }

    [Fact]
    public void SvgWithWrongNamespace_IsLeftUntouched()
    {
        var html = Fence("svg", "<svg xmlns=\"urn:not-svg\"><rect /></svg>");

        var result = Run(html);

        result.Should().Be(html);
        result.Should().NotContain("data-svg-render");
    }

    [Fact]
    public void MissingNamespace_IsAddedForTheRenderedImageOnly()
    {
        var source = "<svg viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" /></svg>";

        var result = Run(Fence("svg", source));

        DecodeDataUrl(result).Should().Contain("xmlns=\"http://www.w3.org/2000/svg\"");
        DecodeTemplate(result).Should().Be(source);
    }

    [Fact]
    public void Title_IsUsedAsAltText()
    {
        var source = "<svg xmlns=\"http://www.w3.org/2000/svg\"><title>A &amp; B</title></svg>";

        var result = Run(Fence("svg", source));

        result.Should().Contain("alt=\"A &amp; B\"");
        result.Should().NotContain("alt=\"SVG image\"");
    }

    [Fact]
    public void CopyAndDownload_CanBeHiddenIndependently()
    {
        var copyHidden = Run(Fence("svg", Svg), n => n.ShowCopy = false);
        copyHidden.Should().NotContain("data-svg-action=\"copy\"");
        copyHidden.Should().Contain("data-svg-action=\"download\"");

        var downloadHidden = Run(Fence("svg", Svg), n => n.ShowDownload = false);
        downloadHidden.Should().NotContain("data-svg-action=\"download\"");
        downloadHidden.Should().Contain("data-svg-action=\"copy\"");
    }

    [Fact]
    public void BothActionsHidden_OmitsToolbar()
    {
        var result = Run(Fence("svg", Svg), n =>
        {
            n.ShowCopy = false;
            n.ShowDownload = false;
        });

        result.Should().Contain("data-svg-render=\"1\"");
        result.Should().NotContain("svg-render-bar");
        result.Should().NotContain("data-svg-action");
    }

    [Fact]
    public void Labels_ComeFromLocService()
    {
        var loc = new Mock<ILocService>();
        loc.Setup(l => l["code.actions.copy"]).Returns("复制");
        loc.Setup(l => l["code.actions.download"]).Returns("下载");
        loc.Setup(l => l["code.actions.copied"]).Returns("已复制");
        loc.Setup(l => l["svg.render.label"]).Returns("矢量图");
        loc.Setup(l => l["svg.render.alt"]).Returns("矢量图图像");

        var result = Run(Fence("svg", Svg), loc: loc.Object);

        result.Should().Contain(">复制</span>");
        result.Should().Contain(">下载</span>");
        result.Should().Contain("data-svg-copied=\"已复制\"");
        result.Should().Contain(">矢量图</span>");
        result.Should().Contain("alt=\"矢量图图像\"");
    }

    [Fact]
    public void SurroundingHtml_AndMultipleBlocks_ArePreserved()
    {
        var html = "<p>before</p>" +
                   Fence("svg", Svg) +
                   "<p>middle</p>" +
                   Fence("xml", Svg) +
                   "<p>after</p>";

        var result = Run(html);

        result.Should().StartWith("<p>before</p><figure");
        result.Should().EndWith("</figure><p>after</p>");
        result.Should().Contain("<p>middle</p>");
        result.Split("data-svg-render=\"1\"").Length.Should().Be(3);
    }

    [Fact]
    public void OversizedBlock_IsSkipped()
    {
        var html = Fence("svg", Svg);

        var result = Run(html, n => n.MaxSvgChars = 10);

        result.Should().Be(html);
    }

    [Fact]
    public void ZeroMaxChars_MeansNoLimit()
    {
        var result = Run(Fence("svg", Svg), n => n.MaxSvgChars = 0);

        result.Should().Contain("data-svg-render=\"1\"");
    }

    [Fact]
    public void RunningTwice_IsIdempotent()
    {
        var once = Run(Fence("svg", Svg));
        var twice = Run(once);

        twice.Should().Be(once);
    }

    [Fact]
    public void SourceCannotBreakOutOfTemplate()
    {
        var source = "<svg xmlns=\"http://www.w3.org/2000/svg\"><title><![CDATA[</template><script>alert(1)</script>]]></title></svg>";
        // 代码块里的源码本身经过 HTML 编码，这里模拟 Markdig 的输出。
        var html = Fence("svg", source);

        var result = Run(html);

        result.Should().Contain("&lt;/template&gt;");
        result.Should().NotContain("</template><script>");
        DecodeTemplate(result).Should().Contain("</template><script>alert(1)</script>");

        // 脚本只存在于源码模板与 base64 数据里，不会成为可执行的页面节点。
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void ExternalEntity_IsNotResolved()
    {
        var html = Fence("xml",
            "<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>&xxe;</text></svg>");

        var result = Run(html);

        result.Should().Be(html);
        result.Should().NotContain("data-svg-render");
    }

    [Fact]
    public void BlockWithoutSvg_AddsNoChangeRecord()
    {
        var env = CreateEnv("<p>hello</p>");

        new SvgRenderNode().Execute(new RenderNodeExecutionContext(env));

        env.ChangeLog.Should().BeEmpty();
        env.GetContent().Should().Be("<p>hello</p>");
    }
}
