namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// GenericNodeEditor 与 OptionsKey 的接线：选项来自插件提供者，
/// 节点属性值变化后触发器同步，提供者无数据时必须显示可见提示。
/// </summary>
public class OptionsKeyIntegrationTests
{
    private static IRenderedComponent<GenericNodeEditor<TestOptionsNode>> RenderEditor(
        BunitContext ctx,
        TestOptionsNode node,
        params INodePropertyOptionsProvider[] providers)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        ctx.Services.AddSingleton(EditorTestHelpers.CreatePluginLoader(providers));

        return ctx.Render<GenericNodeEditor<TestOptionsNode>>(ps => ps
            .Add(p => p.Node, node)
            .Add(p => p.Depth, 0)
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext()));
    }

    [Fact]
    public void OptionsKeyProperty_RendersOptionsSelectWithProviderOptions()
    {
        using var ctx = new BunitContext();
        var node = new TestOptionsNode { Choice = "y" };
        var cut = RenderEditor(ctx, node, new TestOptionsProvider());

        cut.Find(".po-trigger-label").TextContent.Should().Be("Option Y");

        cut.Find(".po-trigger").Click();
        cut.FindAll(".po-item").Select(i => i.TextContent)
            .Should().Contain(t => t.Contains("Option X"));
        cut.FindAll(".po-item-desc").Should().NotBeEmpty();
    }

    [Fact]
    public void SelectingOption_WritesBackToNodeProperty()
    {
        using var ctx = new BunitContext();
        var node = new TestOptionsNode();
        var cut = RenderEditor(ctx, node, new TestOptionsProvider());

        cut.Find(".po-trigger").Click();
        cut.FindAll(".po-item").Single(i => i.TextContent.Contains("Option X")).Click();

        node.Choice.Should().Be("x");
        cut.Find(".po-trigger-label").TextContent.Should().Be("Option X");
    }

    [Fact]
    public void EmptyOptions_ShowsEmptyHintInsteadOfSilentEmptyControl()
    {
        using var ctx = new BunitContext();
        var cut = RenderEditor(ctx, new TestOptionsNode(), new EmptyOptionsProvider());

        cut.Find(".po-hint").TextContent.Should().Be("node_editor.options_empty");
    }

    [Fact]
    public void NoProvider_StillRendersAndShowsHint()
    {
        using var ctx = new BunitContext();
        var cut = RenderEditor(ctx, new TestOptionsNode());

        cut.Find(".po-hint").TextContent.Should().Be("node_editor.options_empty");
    }
}
