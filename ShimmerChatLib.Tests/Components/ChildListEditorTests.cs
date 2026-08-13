using Microsoft.AspNetCore.Components.Web;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// ChildListEditor 列表刷新回归测试：列表内增删即时可见；
/// DropStrip 落点提交后目标列表通过 OnDropCommitted 刷新（覆盖拖放"幽灵节点"修复）。
/// </summary>
public class ChildListEditorTests
{
    private static IRenderedComponent<GenericNodeEditor<TestListNode>> RenderEditor(BunitContext ctx, TestListNode listNode, TreeDragContext? drag = null)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        return ctx.Render<GenericNodeEditor<TestListNode>>(ps => ps
            .Add(p => p.Node, listNode)
            .Add(p => p.Depth, 0)
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext())
            .AddCascadingValue(drag ?? new TreeDragContext()));
    }

    [Fact]
    public void AddingChild_ImmediatelyShowsChild()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode();
        var cut = RenderEditor(ctx, listNode);

        cut.FindAll(".tree-node-card").Should().BeEmpty();

        cut.Find(".ge-children .tn-btn-add").Click();
        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        cut.FindAll(".tree-node-card").Count.Should().Be(1);
        listNode.Children.Should().HaveCount(1);
    }

    [Fact]
    public void RemovingChild_ImmediatelyRemovesChild()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode { Children = { new TestLeafNode() } };
        var cut = RenderEditor(ctx, listNode);

        cut.FindAll(".tree-node-card").Count.Should().Be(1);

        cut.Find(".tn-btn-del").Click();

        cut.FindAll(".tree-node-card").Should().BeEmpty();
        listNode.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task DropIntoEmptyList_ImmediatelyShowsDroppedNode()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode();
        var drag = new TreeDragContext();
        var cut = RenderEditor(ctx, listNode, drag);

        // 模拟从一个外部列表拖入：BeginDrag 建立拖拽状态后直接在落点条上触发 drop
        var leaf = new TestLeafNode();
        drag.BeginDrag(leaf, new List<TestLeafNode> { leaf }, () => Task.CompletedTask);

        cut.Find(".tn-drop-strip").TriggerEvent("ondrop", new DragEventArgs());

        listNode.Children.Should().Contain(leaf);
        cut.FindAll(".tree-node-card").Count.Should().Be(1);
    }
}
