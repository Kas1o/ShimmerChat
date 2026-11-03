using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatBuiltin.Misc.Node.Render.Katex;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// MarkdownRenderNode 的公式渲染行为（KStyleMath / Markdig 管线缓存）。
/// </summary>
public class MarkdownRenderNodeTests
{
    private static string Render(string markdown,
        bool enableMath = true,
        MathOutputMode mathOutput = MathOutputMode.MathML,
        bool enablePipeTables = true)
    {
        var env = new RenderEnv(markdown,
            new Mock<ITreeNodeSerializer>().Object,
            new Mock<IKVDataService>().Object);
        var node = new MarkdownRenderNode
        {
            EnableMath = enableMath,
            MathOutput = mathOutput,
            EnablePipeTables = enablePipeTables
        };

        node.Execute(new RenderNodeExecutionContext(env));
        return env.GetContent();
    }

    [Fact]
    public void InlineMath_ProducesMathMlByDefault()
    {
        var html = Render("欧拉恒等式 $e^{i\\pi}+1=0$ 成立");

        html.Should().Contain("<math");
        html.Should().NotContain("display=\"block\"");
        html.Should().NotContain("$e^{i\\pi}+1=0$");
    }

    [Fact]
    public void InlineMath_KatexHtmlMode_UsesKatexMarkup()
    {
        var html = Render("$x^2$", mathOutput: MathOutputMode.KatexHtml);

        html.Should().Contain("class=\"katex\"");
        html.Should().Contain("katex-html");
    }

    [Fact]
    public void DisplayMath_IsRenderedAsDisplayMath()
    {
        var mathml = Render("$$\\sum_{i=1}^n i$$");
        mathml.Should().Contain("display=\"block\"");

        var katex = Render("$$\n\\frac{1}{2}\n$$", mathOutput: MathOutputMode.KatexHtml);
        katex.Should().Contain("katex-display");
    }

    [Fact]
    public void BackslashDelimiters_AreRendered()
    {
        var html = Render("\\(a+b\\) 与 \\[c+d\\]");

        html.Should().Contain("<math");
        html.Should().Contain("display=\"block\"");
        html.Should().NotContain("\\(a+b\\)");
        html.Should().NotContain("\\[c+d\\]");
    }

    [Fact]
    public void EscapedDollar_IsNotTreatedAsDelimiter()
    {
        var html = Render("价格 \\$5 不变");

        html.Should().Contain("$5");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void CurrencyAmounts_AreNotTreatedAsMath()
    {
        var html = Render("It costs $5 and $10 today");

        html.Should().Contain("$5 and $10");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void UnclosedDelimiter_StaysLiteral()
    {
        var html = Render("未闭合 $x^2");

        html.Should().Contain("$x^2");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void CodeSpan_IsNotParsedAsMath()
    {
        var html = Render("行内代码 `$x^2$` 原样保留");

        html.Should().Contain("<code>$x^2$</code>");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void FencedCodeBlock_IsNotParsedAsMath()
    {
        var html = Render("```\n$a+b$\n```");

        html.Should().Contain("$a+b$");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void MathDisabled_LeavesDelimitersUntouched()
    {
        var html = Render("$x^2$", enableMath: false);

        html.Should().Contain("$x^2$");
        html.Should().NotContain("<math");
    }

    [Fact]
    public void ParseError_ProducesKaTeXErrorMarkup()
    {
        var html = Render("$\\frac{1}$");

        html.Should().Contain("katex-error");
        html.Should().Contain("ParseError");
    }

    [Fact]
    public void PipeTables_StillRender()
    {
        var html = Render("| a | b |\n| - | - |\n| 1 | 2 |");

        html.Should().Contain("<table");
    }
}
