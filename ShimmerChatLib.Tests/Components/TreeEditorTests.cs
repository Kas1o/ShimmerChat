using Bunit;
using ShimmerChatLib.Components;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// TreeEditor 包装器契约：名称/展开状态写在包装器上，展开状态可随树持久化；
/// 负载缺失时显示可见错误，而不是空白卡片。
/// </summary>
public class TreeEditorTests
{
    private static IRenderedComponent<TreeEditor> RenderNode(BunitContext ctx, ITreeNode node)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        ctx.Services.AddSingleton(EditorTestHelpers.CreatePluginLoader());
        return ctx.Render<TreeEditor>(ps => ps
            .Add(p => p.Node, node)
            .Add(p => p.Depth, 1)
            .AddCascadingValue(new TreeDragContext())
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext()));
    }

    [Fact]
    public void ToggleHeader_FlipsWrapperExpansionState()
    {
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper(new TestLeafNode(), "Leaf");
        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-body").Should().NotBeNull();
        wrapper.IsExpanded.Should().BeTrue();

        cut.Find(".tn-header").Click();

        wrapper.IsExpanded.Should().BeFalse();
        cut.FindAll(".tn-body").Should().BeEmpty();

        cut.Find(".tn-header").Click();
        wrapper.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void NameInput_WritesToWrapperNotPayload()
    {
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper(new TestLeafNode(), "Leaf");
        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-name-input").Input("Renamed");

        wrapper.Name.Should().Be("Renamed");
    }

    [Fact]
    public void MissingPayload_ShowsVisibleErrorCard()
    {
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper { Name = "Broken" };

        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-custom-editor-error").TextContent.Should().Contain("tree_editor.payload_missing");
        cut.Find(".tn-label").TextContent.Should().Contain("tree_editor.payload_missing");
    }
}
