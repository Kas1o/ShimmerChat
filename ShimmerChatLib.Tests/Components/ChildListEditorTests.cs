using Microsoft.AspNetCore.Components.Web;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// ChildListEditor 列表编辑测试：任意位置插入（间隙 + 按钮就地展开 NodePickerMenu，
/// 菜单位置即插入位置）、插入后菜单级联后移、过期间隙索引的边界夹取，
/// 以及列表增删/拖放后即时刷新的回归。
/// </summary>
public class ChildListEditorTests
{
    private static IRenderedComponent<GenericNodeEditor<TestListNode>> RenderEditor(BunitContext ctx, TestListNode listNode, TreeDragContext? drag = null)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        ctx.Services.AddSingleton(EditorTestHelpers.CreatePluginLoader());
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
    public void InsertButton_BetweenChildren_InsertsAtThatGap()
    {
        using var ctx = new BunitContext();
        var a = new TestNodeWrapper(new TestLeafNode(), "A");
        var b = new TestNodeWrapper(new TestLeafNode(), "B");
        var listNode = new TestListNode { Children = { a, b } };
        var cut = RenderEditor(ctx, listNode);

        // 2 个子节点 → 3 个间隙（0/1/2），每个间隙一条插入行
        cut.FindAll(".tn-insert-btn").Count.Should().Be(3);

        // 打开 A 与 B 之间的间隙菜单，选择器就地展开在该间隙内
        cut.FindAll(".tn-insert-btn")[1].Click();
        cut.FindAll(".tn-add-menu").Count.Should().Be(1);
        cut.FindAll(".tn-insert-row")[1].QuerySelector(".tn-add-menu").Should().NotBeNull();

        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        // 新节点落在间隙 1：A、NEW、B —— 而不是追加到末尾
        listNode.Children.Count.Should().Be(3);
        listNode.Children[0].Should().BeSameAs(a);
        listNode.Children[2].Should().BeSameAs(b);
        listNode.Children[1].Should().NotBeSameAs(a);
        listNode.Children[1].Should().NotBeSameAs(b);
        listNode.Children[1].Node.Should().BeOfType<TestLeafNode>();
        cut.FindAll(".tree-node-card").Count.Should().Be(3);
    }

    [Fact]
    public void InsertMenu_Cascades_ConsecutiveInsertsStayInOrder()
    {
        using var ctx = new BunitContext();
        var a = new TestNodeWrapper(new TestLeafNode(), "A");
        var listNode = new TestListNode { Children = { a } };
        var cut = RenderEditor(ctx, listNode);

        // 间隙 0 插入第一个节点
        cut.FindAll(".tn-insert-btn")[0].Click();
        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        // 菜单保持展开并后移一位：直接再选一次，第二个节点排在第一个之后、A 之前
        cut.FindAll(".tn-add-menu").Count.Should().Be(1);
        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        listNode.Children.Count.Should().Be(3);
        listNode.Children[0].Node.Should().BeOfType<TestLeafNode>();
        listNode.Children[1].Node.Should().BeOfType<TestLeafNode>();
        listNode.Children[0].Should().NotBeSameAs(listNode.Children[1]);
        listNode.Children[2].Should().BeSameAs(a);
    }

    [Fact]
    public void HeaderAddButton_MenuOpensAtEndGap_AndAppends()
    {
        using var ctx = new BunitContext();
        var a = new TestNodeWrapper(new TestLeafNode(), "A");
        var listNode = new TestListNode { Children = { a } };
        var cut = RenderEditor(ctx, listNode);

        cut.Find(".ge-children .tn-btn-add").Click();

        // 头部 + 的菜单展开在末尾间隙，与追加语义一致（菜单位置即插入位置）
        cut.FindAll(".tn-insert-row")[1].QuerySelector(".tn-add-menu").Should().NotBeNull();

        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        listNode.Children.Count.Should().Be(2);
        listNode.Children[0].Should().BeSameAs(a);
        listNode.Children[1].Node.Should().BeOfType<TestLeafNode>();
    }

    [Fact]
    public void InsertChild_StaleGapIndex_ClampsIntoValidRange()
    {
        using var ctx = new BunitContext();
        var a = new TestNodeWrapper(new TestLeafNode(), "A");
        var b = new TestNodeWrapper(new TestLeafNode(), "B");
        var listNode = new TestListNode { Children = { a, b } };
        var cut = RenderEditor(ctx, listNode);

        // 菜单展开在末尾间隙（索引 2）
        cut.Find(".ge-children .tn-btn-add").Click();

        // 菜单开着时外部删掉一个子节点：间隙索引 2 已越界（此时 Count=1）
        listNode.Children.Remove(a);

        cut.Find(".tn-add-cat").Click();
        cut.Find(".tn-add-item").Click();

        // 越界索引被夹回有效区间，插入不抛异常，落在列表末尾
        listNode.Children.Count.Should().Be(2);
        listNode.Children[0].Should().BeSameAs(b);
        listNode.Children[1].Node.Should().BeOfType<TestLeafNode>();
    }

    [Fact]
    public void InsertButton_TogglesMenuClosedOnSecondClick()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode();
        var cut = RenderEditor(ctx, listNode);

        cut.FindAll(".tn-insert-btn")[0].Click();
        cut.FindAll(".tn-add-menu").Count.Should().Be(1);

        cut.FindAll(".tn-insert-btn")[0].Click();
        cut.FindAll(".tn-add-menu").Should().BeEmpty();
    }

    [Fact]
    public void InsertMenu_EscapeKeyClosesMenu()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode();
        var cut = RenderEditor(ctx, listNode);

        cut.FindAll(".tn-insert-btn")[0].Click();
        cut.FindAll(".tn-add-menu").Count.Should().Be(1);

        cut.Find(".tn-add-search").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tn-add-menu").Should().BeEmpty();
        listNode.Children.Should().BeEmpty();
    }

    [Fact]
    public void RemovingChild_ImmediatelyRemovesChild()
    {
        using var ctx = new BunitContext();
        var listNode = new TestListNode { Children = { new TestNodeWrapper(new TestLeafNode()) } };
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
        var leaf = new TestNodeWrapper(new TestLeafNode());
        drag.BeginDrag(leaf, new List<TestNodeWrapper> { leaf }, () => Task.CompletedTask);

        cut.Find(".tn-drop-strip").TriggerEvent("ondrop", new DragEventArgs());

        listNode.Children.Should().Contain(leaf);
        cut.FindAll(".tree-node-card").Count.Should().Be(1);
    }
}
