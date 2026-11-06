using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// CodeHighlightNode：代码块扫描、跳过着色、幂等与降级行为。
/// </summary>
public class CodeHighlightNodeTests
{
    private static RenderEnv CreateEnv(string html) =>
        new(html, new Mock<ITreeNodeSerializer>().Object, new Mock<IKVDataService>().Object);

    private static string Run(string html, Action<CodeHighlightNode>? configure = null)
    {
        var env = CreateEnv(html);
        var node = new CodeHighlightNode();
        configure?.Invoke(node);
        node.Execute(new RenderNodeExecutionContext(env));
        return env.GetContent();
    }

    private static string Fence(string language, string encodedCode) =>
        $"<pre><code class=\"language-{language}\">{encodedCode}</code></pre>";

    [Fact]
    public void CSharpBlock_IsHighlighted()
    {
        var html = Fence("csharp", "public class Foo { }");

        var result = Run(html);

        result.Should().Contain("hljs-keyword");
        result.Should().Contain("<pre><code class=\"language-csharp\">");
        result.Should().Contain("</code></pre>");
    }

    [Fact]
    public void EndToEnd_MarkdownRenderThenHighlight()
    {
        var env = CreateEnv("```cs\npublic class Foo\n{\n}\n```");
        new MarkdownRenderNode().Execute(new RenderNodeExecutionContext(env));
        new CodeHighlightNode().Execute(new RenderNodeExecutionContext(env));

        var html = env.GetContent();
        html.Should().Contain("<pre><code class=\"language-cs\">");
        html.Should().Contain("hljs-keyword");
        html.Should().Contain("hljs-title");
    }

    [Fact]
    public void SvgBlock_IsSkippedByDefault()
    {
        var html = Fence("svg", "&lt;svg viewBox=\"0 0 1 1\"&gt;&lt;/svg&gt;");

        var result = Run(html);

        result.Should().Be(html);
        result.Should().NotContain("hljs-");
    }

    [Fact]
    public void SvgBlock_IsHighlightedWhenSkipListIsCleared()
    {
        var html = Fence("svg", "&lt;svg&gt;&lt;/svg&gt;");

        var result = Run(html, n => n.SkipLanguages = "");

        result.Should().Contain("hljs-");
    }

    [Fact]
    public void SkipList_IsCaseInsensitiveAndSeparatorTolerant()
    {
        var html = Fence("SVG", "&lt;svg&gt;&lt;/svg&gt;");

        var result = Run(html, n => n.SkipLanguages = "  csharp ; Mermaid , svg ");

        result.Should().Be(html);
    }

    [Fact]
    public void SkippedBlock_DoesNotPreventOtherBlocksFromHighlighting()
    {
        var html = "<p>before</p>" +
                   Fence("svg", "&lt;svg&gt;&lt;/svg&gt;") +
                   "<p>middle</p>" +
                   Fence("csharp", "public class Foo { }") +
                   "<p>after</p>";

        var result = Run(html);

        result.Should().Contain(Fence("svg", "&lt;svg&gt;&lt;/svg&gt;"));
        result.Should().Contain("hljs-keyword");
        result.Should().StartWith("<p>before</p>");
        result.Should().EndWith("<p>after</p>");
        result.Should().Contain("<p>middle</p>");
    }

    [Fact]
    public void UnknownLanguage_LeavesBlockUntouchedAndEscaped()
    {
        var html = Fence("foobar", "&lt;script&gt;alert(1)&lt;/script&gt;");

        var result = Run(html);

        result.Should().Be(html);
        result.Should().NotContain("<script>");
    }

    [Fact]
    public void BlockWithoutLanguage_StaysUntouchedByDefault()
    {
        var html = "<pre><code>public class Foo { }</code></pre>";

        var result = Run(html);

        result.Should().Be(html);
        result.Should().NotContain("hljs-");
    }

    [Fact]
    public void DefaultLanguage_AppliesToBlockWithoutLanguage()
    {
        var html = "<pre><code>public class Foo { }</code></pre>";

        var result = Run(html, n => n.DefaultLanguage = "csharp");

        result.Should().Contain("hljs-keyword");
    }

    [Fact]
    public void OversizedBlock_IsSkipped()
    {
        var html = Fence("csharp", new string('a', 200));

        var result = Run(html, n => n.MaxBlockChars = 50);

        result.Should().Be(html);
        result.Should().NotContain("hljs-");
    }

    [Fact]
    public void ZeroMaxBlockChars_MeansNoLimit()
    {
        var html = Fence("csharp", "public class Foo { }");

        var result = Run(html, n => n.MaxBlockChars = 0);

        result.Should().Contain("hljs-keyword");
    }

    [Fact]
    public void RunningTwice_DoesNotDoubleHighlight()
    {
        var html = Fence("csharp", "public class Foo { }");

        var once = Run(html);
        var twice = Run(once);

        twice.Should().Be(once);
        CountOccurrences(twice, "hljs-keyword").Should().Be(CountOccurrences(once, "hljs-keyword"));
    }

    [Fact]
    public void PreWithoutCode_IsUntouched()
    {
        var html = "<pre>plain text</pre>";

        var result = Run(html);

        result.Should().Be(html);
    }

    [Fact]
    public void DocumentWithoutCodeBlocks_AddsNoChangeRecord()
    {
        var env = CreateEnv("<p>hello</p>");

        new CodeHighlightNode().Execute(new RenderNodeExecutionContext(env));

        env.ChangeLog.Should().BeEmpty();
        env.GetContent().Should().Be("<p>hello</p>");
    }

    [Fact]
    public void EntitiesInsideCodeBlock_StayEscaped()
    {
        var html = Fence("csharp", "var a = \"&lt;b&gt; &amp; c\";");

        var result = Run(html);

        result.Should().Contain("hljs-");
        result.Should().Contain("&lt;b&gt;");
        result.Should().NotContain("<b>");
    }

    [Fact]
    public void PlainPreTextAroundCode_IsPreserved()
    {
        var html = "<pre>prefix<code class=\"language-csharp\">public class Foo { }</code>suffix</pre>";

        var result = Run(html);

        result.Should().Contain("prefix");
        result.Should().Contain("suffix");
        result.Should().Contain("hljs-keyword");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
