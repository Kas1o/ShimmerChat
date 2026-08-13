using ShimmerChatLib.Components;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests.Components;

/// <summary>TreeEditorContext：节点创建、剪贴板复制/粘贴（Id 再生成）。</summary>
public class TreeEditorContextTests
{
    [Fact]
    public void CreateNode_CreatesInstance_WithNodeInfoLabelAsName()
    {
        var context = EditorTestHelpers.CreateEditorContext();

        var node = context.CreateNode(typeof(TestLeafNode));

        node.Should().BeOfType<TestLeafNode>();
        node.Name.Should().Be("test.leaf_node");
    }

    [Fact]
    public void CopyThenPaste_RegeneratesAllIds()
    {
        var original = new TestSlotNode { Then = new TestLeafNode() };
        // 模拟序列化器返回的"反序列化副本"：Id 与原节点相同，粘贴时应全部重新生成
        var clone = new TestSlotNode
        {
            Id = original.Id,
            Name = original.Name,
            Then = new TestLeafNode { Id = original.Then.Id }
        };
        var serializer = new Mock<ITreeNodeSerializer>();
        serializer.Setup(s => s.Serialize(It.IsAny<ITreeNode>())).Returns("json");
        serializer.Setup(s => s.Deserialize("json")).Returns(clone);

        var context = new TreeEditorContext(serializer.Object, Mock.Of<INodeTypeCatalog>(), typeof(ITreeNode));

        context.HasClipboardContent.Should().BeFalse();
        context.Copy(original);
        context.HasClipboardContent.Should().BeTrue();

        var pasted = context.Paste();

        pasted.Should().BeSameAs(clone);
        pasted!.Id.Should().NotBe(original.Id);
        ((TestSlotNode)pasted).Then!.Id.Should().NotBe(original.Then!.Id);
    }

    [Fact]
    public void Paste_WithoutCopy_ReturnsNull()
    {
        var context = new TreeEditorContext(Mock.Of<ITreeNodeSerializer>(), Mock.Of<INodeTypeCatalog>(), typeof(ITreeNode));

        context.Paste().Should().BeNull();
        context.HasClipboardContent.Should().BeFalse();
    }

    [Fact]
    public void Copy_RaisesClipboardChanged()
    {
        var context = EditorTestHelpers.CreateEditorContext();
        var fired = 0;
        context.ClipboardChanged += () => fired++;

        context.Copy(new TestLeafNode());

        fired.Should().Be(1);
    }
}
