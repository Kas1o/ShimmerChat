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

    [Fact]
    public void BypassButton_TogglesWrapperBypassState()
    {
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper(new TestLeafNode(), "Leaf");
        var cut = RenderNode(ctx, wrapper);

        wrapper.IsBypassed.Should().BeFalse();
        cut.Find(".tree-node-card").ClassList.Should().NotContain("tn-bypassed");

        cut.Find(".tn-btn-bypass").Click();

        wrapper.IsBypassed.Should().BeTrue();
        cut.Find(".tree-node-card").ClassList.Should().Contain("tn-bypassed");
        cut.Find(".tn-btn-bypass").ClassList.Should().Contain("is-active");

        cut.Find(".tn-btn-bypass").Click();

        wrapper.IsBypassed.Should().BeFalse();
        cut.Find(".tree-node-card").ClassList.Should().NotContain("tn-bypassed");
        cut.Find(".tn-btn-bypass").ClassList.Should().NotContain("is-active");
    }

    [Fact]
    public void BypassButton_Click_DoesNotToggleExpansion()
    {
        // 旁路按钮 stopPropagation：点击不能顺带折叠/展开节点，否则一次旁路会误改编辑视图。
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper(new TestLeafNode(), "Leaf");
        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-btn-bypass").Click();

        wrapper.IsExpanded.Should().BeTrue();
        cut.FindAll(".tn-body").Should().NotBeEmpty();
    }

    [Fact]
    public void BypassButton_OnMissingPayloadCard_TogglesToo()
    {
        // 损坏节点（负载缺失）也能旁路：执行阶段包装器会跳过它，插件恢复前不再报错。
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper { Name = "Broken" };
        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-btn-bypass").Click();

        wrapper.IsBypassed.Should().BeTrue();
        cut.Find(".tree-node-card").ClassList.Should().Contain("tn-bypassed");
        // 数据缺失的可见错误仍然保留，旁路不掩盖数据问题本身。
        cut.Find(".tn-custom-editor-error").TextContent.Should().Contain("tree_editor.payload_missing");
    }

    [Fact]
    public void BypassButton_TitleReflectsCurrentState()
    {
        using var ctx = new BunitContext();
        var wrapper = new TestNodeWrapper(new TestLeafNode(), "Leaf") { IsBypassed = true };
        var cut = RenderNode(ctx, wrapper);

        cut.Find(".tn-btn-bypass").Attributes["title"]!.Value.Should().Be("tree_editor.unbypass");

        cut.Find(".tn-btn-bypass").Click();

        cut.Find(".tn-btn-bypass").Attributes["title"]!.Value.Should().Be("tree_editor.bypass");
    }
}
