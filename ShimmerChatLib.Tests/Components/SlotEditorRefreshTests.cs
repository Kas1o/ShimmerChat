using Microsoft.AspNetCore.Components.Web;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// 回归测试：SlotEditor 通过 OnSet/OnClear（EventCallback）让父组件（GenericNodeEditor）重渲染，
/// 覆盖"条件节点无自动刷新"修复（新增/删除 Then/Else 子节点后槽位立即更新）。
/// 修复前 OnSet 为 Action：只刷新 SlotEditor 自身，SlotNode 参数保持旧值，子节点不出现。
/// </summary>
public class SlotEditorRefreshTests
{
    private static IRenderedComponent<GenericNodeEditor<TestSlotNode>> RenderEditor(BunitContext ctx, TestSlotNode slotNode, TreeDragContext? drag = null)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        return ctx.Render<GenericNodeEditor<TestSlotNode>>(ps => ps
            .Add(p => p.Node, slotNode)
            .Add(p => p.Depth, 0)
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext())
            .AddCascadingValue(drag ?? new TreeDragContext()));
    }

    [Fact]
    public void AddingNodeToSlot_ImmediatelyShowsChild()
    {
        using var ctx = new BunitContext();
        var slotNode = new TestSlotNode();
        var cut = RenderEditor(ctx, slotNode);

        cut.FindAll(".tree-node-card").Should().BeEmpty();

        // 打开 Then 槽位添加菜单 → 进入 category.test → 选择叶子节点
        cut.Find(".ge-children .tn-btn-add").Click();
        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        // 修复前：OnSet 为 Action，父组件不刷新，SlotNode 参数保持 null，子节点不出现。
        // 修复后：EventCallback 触发父组件重渲染，槽位立即显示新节点。
        cut.FindAll(".tree-node-card").Count.Should().Be(1);
        cut.Find(".tn-label").TextContent.Should().Be("test.leaf_node");
        slotNode.Then.Should().NotBeNull();
    }

    [Fact]
    public void RemovingNodeFromSlot_ImmediatelyEmptiesSlot()
    {
        using var ctx = new BunitContext();
        var slotNode = new TestSlotNode { Then = new TestLeafNode() };
        var cut = RenderEditor(ctx, slotNode);

        cut.FindAll(".tree-node-card").Count.Should().Be(1);

        cut.Find(".tn-btn-del").Click();

        cut.FindAll(".tree-node-card").Should().BeEmpty();
        cut.FindAll(".tn-empty").Count.Should().Be(1);
        slotNode.Then.Should().BeNull();
    }
}
