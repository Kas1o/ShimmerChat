using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// CodeBlockActionsNode：工具条结构、开关、幂等与本地化文案。
/// </summary>
public class CodeBlockActionsNodeTests
{
    private const string CSharpBlock =
        "<pre><code class=\"language-csharp\">public class Foo { }</code></pre>";

    private static RenderEnv CreateEnv(string html, ILocService? loc = null) =>
        new(html, new Mock<ITreeNodeSerializer>().Object, new Mock<IKVDataService>().Object,
            chat: null, agent: null, loc: loc);

    private static string Run(string html, Action<CodeBlockActionsNode>? configure = null,
        ILocService? loc = null)
    {
        var env = CreateEnv(html, loc);
        var node = new CodeBlockActionsNode();
        configure?.Invoke(node);
        node.Execute(new RenderNodeExecutionContext(env));
        return env.GetContent();
    }

    [Fact]
    public void Block_IsWrappedWithToolbar()
    {
        var result = Run(CSharpBlock);

        result.Should().Contain("<div class=\"code-block\">");
        result.Should().Contain("class=\"code-block-head\"");
        result.Should().Contain("<span class=\"code-block-lang\">csharp</span>");
        result.Should().Contain("data-code-action=\"copy\"");
        result.Should().Contain("data-code-action=\"download\"");
        result.Should().Contain("data-code-filename=\"snippet.cs\"");
        result.Should().EndWith("</div>");
    }

    [Fact]
    public void OriginalPreAndContent_ArePreserved()
    {
        var result = Run(CSharpBlock);

        result.Should().Contain(
            "<pre data-code-block=\"1\"><code class=\"language-csharp\">public class Foo { }</code></pre>");
    }

    [Fact]
    public void PreAttributes_ArePreservedWhenMarkerIsInserted()
    {
        var html = "<pre class=\"extra\" id=\"x\"><code class=\"language-js\">let a = 1;</code></pre>";

        var result = Run(html);

        result.Should().Contain("<pre data-code-block=\"1\" class=\"extra\" id=\"x\">");
    }

    [Fact]
    public void RunningTwice_DoesNotDoubleWrap()
    {
        var once = Run(CSharpBlock);
        var twice = Run(once);

        twice.Should().Be(once);
        twice.Split("code-block-head").Length.Should().Be(2);
    }

    [Fact]
    public void CopyButton_CanBeHidden()
    {
        var result = Run(CSharpBlock, n => n.ShowCopy = false);

        result.Should().NotContain("data-code-action=\"copy\"");
        result.Should().Contain("data-code-action=\"download\"");
    }

    [Fact]
    public void DownloadButton_CanBeHidden()
    {
        var result = Run(CSharpBlock, n => n.ShowDownload = false);

        result.Should().NotContain("data-code-action=\"download\"");
        result.Should().Contain("data-code-action=\"copy\"");
    }

    [Fact]
    public void LanguageLabel_CanBeHidden()
    {
        var result = Run(CSharpBlock, n => n.ShowLanguage = false);

        result.Should().Contain("<span class=\"code-block-lang\"></span>");
        result.Should().NotContain(">csharp</span>");
    }

    [Fact]
    public void UnknownLanguage_ProducesTextExtension()
    {
        var html = "<pre><code class=\"language-whatever\">x</code></pre>";

        var result = Run(html);

        result.Should().Contain("data-code-filename=\"snippet.txt\"");
    }

    [Fact]
    public void Labels_ComeFromLocService()
    {
        var loc = new Mock<ILocService>();
        loc.Setup(l => l["code.actions.copy"]).Returns("复制");
        loc.Setup(l => l["code.actions.download"]).Returns("下载");
        loc.Setup(l => l["code.actions.copied"]).Returns("已复制");

        var result = Run(CSharpBlock, loc: loc.Object);

        result.Should().Contain(">复制</span>");
        result.Should().Contain(">下载</span>");
        result.Should().Contain("data-code-copied=\"已复制\"");
    }

    [Fact]
    public void Labels_FallBackToEnglishWithoutLocService()
    {
        var result = Run(CSharpBlock);

        result.Should().Contain(">Copy</span>");
        result.Should().Contain(">Download</span>");
    }

    [Fact]
    public void SurroundingHtml_IsPreserved()
    {
        var html = "<p>before</p>" + CSharpBlock + "<p>after</p>";

        var result = Run(html);

        result.Should().StartWith("<p>before</p><div class=\"code-block\">");
        result.Should().EndWith("</div><p>after</p>");
    }

    [Fact]
    public void MultipleBlocks_EachGetTheirOwnToolbar()
    {
        var html = CSharpBlock + "<p>middle</p>" +
                   "<pre><code class=\"language-python\">print(1)</code></pre>";

        var result = Run(html);

        result.Should().Contain("code-block-lang\">csharp");
        result.Should().Contain("code-block-lang\">python");
        result.Split("code-block-head").Length.Should().Be(3);
    }

    [Fact]
    public void DocumentWithoutCodeBlocks_AddsNoChangeRecord()
    {
        var env = CreateEnv("<p>hello</p>");

        new CodeBlockActionsNode().Execute(new RenderNodeExecutionContext(env));

        env.ChangeLog.Should().BeEmpty();
        env.GetContent().Should().Be("<p>hello</p>");
    }

    [Fact]
    public void WorksTogetherWithHighlightNode_InEitherOrder()
    {
        var markdown = "```csharp\npublic class Foo\n{\n}\n```";

        var first = new RenderEnv(markdown, new Mock<ITreeNodeSerializer>().Object,
            new Mock<IKVDataService>().Object);
        new MarkdownRenderNode().Execute(new RenderNodeExecutionContext(first));
        new CodeHighlightNode().Execute(new RenderNodeExecutionContext(first));
        new CodeBlockActionsNode().Execute(new RenderNodeExecutionContext(first));

        var second = new RenderEnv(markdown, new Mock<ITreeNodeSerializer>().Object,
            new Mock<IKVDataService>().Object);
        new MarkdownRenderNode().Execute(new RenderNodeExecutionContext(second));
        new CodeBlockActionsNode().Execute(new RenderNodeExecutionContext(second));
        new CodeHighlightNode().Execute(new RenderNodeExecutionContext(second));

        var highlightedThenTools = first.GetContent();
        var toolsThenHighlighted = second.GetContent();

        foreach (var html in new[] { highlightedThenTools, toolsThenHighlighted })
        {
            html.Should().Contain("hljs-keyword");
            html.Should().Contain("data-code-action=\"copy\"");
            html.Should().Contain("data-code-block=\"1\"");
        }

        // 工具条结构一致，只有 <code> 内文的高亮方式可能不同
        highlightedThenTools.Should().Contain("<span class=\"code-block-lang\">csharp</span>");
        toolsThenHighlighted.Should().Contain("<span class=\"code-block-lang\">csharp</span>");
    }
}
