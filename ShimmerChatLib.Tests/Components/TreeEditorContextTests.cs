using ShimmerChatLib.Components;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests.Components;

/// <summary>TreeEditorContext：节点创建、剪贴板复制/粘贴（Id 再生成）。</summary>
public class TreeEditorContextTests
{
    [Fact]
    public void CreateNode_CreatesWrapperWithNodeInfoLabelAsName()
    {
        var context = EditorTestHelpers.CreateEditorContext();

        var node = context.CreateNode(typeof(TestLeafNode));

        node.Should().BeOfType<TestNodeWrapper>();
        node.Payload.Should().BeOfType<TestLeafNode>();
        node.Name.Should().Be("test.leaf_node");
        node.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void CopyThenPaste_RegeneratesAllIds()
    {
        var originalLeaf = new TestNodeWrapper(new TestLeafNode(), "Leaf");
        var original = new TestNodeWrapper(new TestSlotNode { Then = originalLeaf }, "Slot");
        // 模拟序列化器返回的"反序列化副本"：Id 与原节点相同，粘贴时应全部重新生成
        var clone = new TestNodeWrapper(new TestSlotNode
        {
            Then = new TestNodeWrapper(new TestLeafNode(), "Leaf") { Id = originalLeaf.Id }
        }, "Slot")
        {
            Id = original.Id
        };
        var serializer = new Mock<ITreeNodeSerializer>();
        serializer.Setup(s => s.Serialize(It.IsAny<ITreeNode>())).Returns("json");
        serializer.Setup(s => s.Deserialize("json")).Returns(clone);

        var context = new TreeEditorContext(serializer.Object, Mock.Of<INodeTypeCatalog>(),
            typeof(ITreeNode), typeof(TestNodeWrapper));

        context.HasClipboardContent.Should().BeFalse();
        context.Copy(original);
        context.HasClipboardContent.Should().BeTrue();

        var pasted = context.Paste();

        pasted.Should().BeSameAs(clone);
        pasted!.Id.Should().NotBe(original.Id);
        var pastedChild = ((TestSlotNode)((TestNodeWrapper)pasted!).Node!).Then!;
        var originalChild = ((TestSlotNode)original.Node!).Then!;
        pastedChild.Id.Should().NotBe(originalChild.Id);
    }

    [Fact]
    public void Paste_WithoutCopy_ReturnsNull()
    {
        var context = new TreeEditorContext(Mock.Of<ITreeNodeSerializer>(), Mock.Of<INodeTypeCatalog>(),
            typeof(ITreeNode), typeof(TestNodeWrapper));

        context.Paste().Should().BeNull();
        context.HasClipboardContent.Should().BeFalse();
    }

    [Fact]
    public void Copy_RaisesClipboardChanged()
    {
        var context = EditorTestHelpers.CreateEditorContext();
        var fired = 0;
        context.ClipboardChanged += () => fired++;

        context.Copy(new TestNodeWrapper(new TestLeafNode()));

        fired.Should().Be(1);
    }
}
