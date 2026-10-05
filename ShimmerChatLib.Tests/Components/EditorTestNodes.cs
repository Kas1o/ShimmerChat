using ShimmerChatLib.Components;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// 带单节点槽位（Then）的测试节点，结构对应 IfNode / AdvancedIfNode 的 Then/Else 槽位。
/// </summary>
[NodeInfo("test.slot_node", Icon = "S", Color = "#000000", CategoryKeys = ["category.test"])]
public class TestSlotNode : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Slot";

    [NodeProperty("test.slot_node.then", Order = 1)]
    public ITreeNode? Then { get; set; }
}

/// <summary>
/// 带子节点列表的测试节点，结构对应 SequenceNode 等列表型节点。
/// </summary>
[NodeInfo("test.list_node", Icon = "L", Color = "#000000", CategoryKeys = ["category.test"])]
public class TestListNode : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "List";

    [NodeProperty("test.list_node.children", Order = 1)]
    public List<TestLeafNode> Children { get; set; } = new();
}

/// <summary>可作为槽位/列表子节点的叶子节点。</summary>
[NodeInfo("test.leaf_node", Icon = "F", Color = "#000000", CategoryKeys = ["category.test"])]
public class TestLeafNode : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Leaf";
}

/// <summary>通用递归树节点（自引用子列表），用于 TreeDragContext 拖拽逻辑测试。</summary>
public class TestTree : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Node";
    public List<TestTree> Children { get; set; } = new();
}

/// <summary>节点编辑器测试共享辅助。</summary>
public static class EditorTestHelpers
{
    /// <summary>构造编辑器上下文：目录只提供 TestLeafNode，序列化器用空 mock。</summary>
    public static TreeEditorContext CreateEditorContext()
    {
        var catalog = new Mock<INodeTypeCatalog>();
        catalog.Setup(c => c.GetNodeTypes(It.IsAny<Type>()))
            .Returns(new List<NodeTypeMetadata>
            {
                new("test.leaf_node", typeof(TestLeafNode), "F", "#000000", null, "category.test"),
            });
        return new TreeEditorContext(Mock.Of<ITreeNodeSerializer>(), catalog.Object, typeof(ITreeNode));
    }

    /// <summary>返回 key 本身的 ILocService：组件渲染时直接显示本地化 key。</summary>
    public static ILocService CreateLocService()
    {
        var loc = new Mock<ILocService>();
        loc.Setup(s => s[It.IsAny<string>()]).Returns((string key) => key);
        return loc.Object;
    }
}
