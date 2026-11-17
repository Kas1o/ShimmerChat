using ShimmerChatLib.Components;

namespace ShimmerChatLib.Tests.Components;

/// <summary>TreeDragContext 拖拽提交/移除的纯逻辑测试（不涉及渲染）。</summary>
public class TreeDragContextTests
{
    private static TestNodeWrapper NewNode(string? name = null)
        => new(new TestTree(), name);

    private static List<TestNodeWrapper> ChildrenOf(TestNodeWrapper node)
        => ((TestTree)node.Node!).Children;

    [Fact]
    public async Task CommitDrop_MovesNodeBetweenLists()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var source = new List<TestNodeWrapper> { a, b };
        var target = new List<TestNodeWrapper>();
        var drag = new TreeDragContext();
        var notified = false;

        drag.BeginDrag(a, source, () => { notified = true; return Task.CompletedTask; });
        var ok = await drag.CommitDrop(target, 0);

        ok.Should().BeTrue();
        source.Should().Equal(b);
        target.Should().Equal(a);
        notified.Should().BeTrue();
        drag.IsDragging.Should().BeFalse(); // CommitDrop 成功后结束拖拽
    }

    [Fact]
    public async Task CommitDrop_ReordersWithinSameList()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var c = NewNode("C");
        var list = new List<TestNodeWrapper> { a, b, c };
        var drag = new TreeDragContext();

        // 把 a 拖到 c 之前（插入索引 2）
        drag.BeginDrag(a, list, () => Task.CompletedTask);
        var ok = await drag.CommitDrop(list, 2);

        ok.Should().BeTrue();
        list.Should().Equal(b, a, c);

        // 把 b 拖到末尾（插入索引 3，此时列表 [b, a, c] 中 b 在索引 0）
        drag.BeginDrag(b, list, () => Task.CompletedTask);
        ok = await drag.CommitDrop(list, 3);

        ok.Should().BeTrue();
        list.Should().Equal(a, c, b);

        // 节点已在末尾时拖到末尾是无操作
        drag.BeginDrag(b, list, () => Task.CompletedTask);
        ok = await drag.CommitDrop(list, 3);
        ok.Should().BeFalse();
        list.Should().Equal(a, c, b);
    }

    [Fact]
    public async Task CommitDrop_NoopWhenDroppingAtSamePosition()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var list = new List<TestNodeWrapper> { a, b };
        var drag = new TreeDragContext();

        drag.BeginDrag(a, list, () => Task.CompletedTask);
        (await drag.CommitDrop(list, 0)).Should().BeFalse();
        drag.BeginDrag(a, list, () => Task.CompletedTask);
        (await drag.CommitDrop(list, 1)).Should().BeFalse();

        list.Should().Equal(a, b);
    }

    [Fact]
    public async Task CommitDrop_PreventsDroppingIntoOwnDescendant()
    {
        var root = NewNode();
        var child = NewNode();
        ChildrenOf(root).Add(child);
        var source = new List<TestNodeWrapper> { root };
        var drag = new TreeDragContext();

        drag.BeginDrag(root, source, () => Task.CompletedTask);
        var ok = await drag.CommitDrop(ChildrenOf(child), 0);

        ok.Should().BeFalse();
        ChildrenOf(child).Should().BeEmpty();
        source.Should().Equal(root); // 未提交，源列表不变
    }

    [Fact]
    public async Task CommitDrop_FromSlotSource_InvokesRemoveAction()
    {
        var node = NewNode();
        var cleared = false;
        var target = new List<TestNodeWrapper>();
        var drag = new TreeDragContext();

        drag.BeginDrag(node, () => { cleared = true; return Task.CompletedTask; }, () => Task.CompletedTask);
        var ok = await drag.CommitDrop(target, 0);

        ok.Should().BeTrue();
        cleared.Should().BeTrue();
        target.Should().Equal(node);
    }

    [Fact]
    public async Task RemoveFromSource_RemovesFromList_AndNotifies()
    {
        var a = NewNode("A");
        var b = NewNode("B");
        var list = new List<TestNodeWrapper> { a, b };
        var drag = new TreeDragContext();
        var notified = false;

        drag.BeginDrag(a, list, () => { notified = true; return Task.CompletedTask; });
        await drag.RemoveFromSource();

        list.Should().Equal(b);
        notified.Should().BeTrue();
        drag.IsDragging.Should().BeTrue(); // RemoveFromSource 不结束拖拽
    }

    [Fact]
    public void IsAncestorOf_DetectsDescendants()
    {
        var root = NewNode();
        var child = NewNode();
        var leaf = NewNode();
        ChildrenOf(root).Add(child);
        ChildrenOf(child).Add(leaf);

        TreeDragContext.IsAncestorOf(root, leaf).Should().BeTrue();
        TreeDragContext.IsAncestorOf(root, child).Should().BeTrue();
        TreeDragContext.IsAncestorOf(child, leaf).Should().BeTrue();
        TreeDragContext.IsAncestorOf(leaf, root).Should().BeFalse();
        TreeDragContext.IsAncestorOf(child, root).Should().BeFalse();
    }
}
