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

/// <summary>带动态选项属性的测试节点（对应 OptionsKey 迁移后的节点形态）。</summary>
[NodeInfo("test.options_node", Icon = "O", Color = "#000000", CategoryKeys = ["category.test"])]
public class TestOptionsNode : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Options";

    [NodeProperty("test.options_node.choice", Order = 1, OptionsKey = TestOptionsProvider.KeyName,
        ShowOptionDescription = true, OptionsDefaultLabelKey = "test.options_node.none")]
    public string Choice { get; set; } = "";
}

/// <summary>固定选项提供者，用于验证 OptionsKey → PropertyOptionsSelect 的接线。</summary>
public sealed class TestOptionsProvider : INodePropertyOptionsProvider
{
    public const string KeyName = "test_options";

    public string Key => KeyName;

    public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services) =>
    [
        new("x", "Option X", Group: "G1", Description: "desc x"),
        new("y", "Option Y", Group: "G1", Description: "desc y")
    ];
}

/// <summary>空选项提供者：覆盖「有 OptionsKey 但无数据」的降级路径。</summary>
public sealed class EmptyOptionsProvider : INodePropertyOptionsProvider
{
    public string Key => TestOptionsProvider.KeyName;

    public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services) => [];
}

/// <summary>带图像列表属性的测试节点（Editor = ImageList 的自动表单接线）。</summary>
[NodeInfo("test.image_node", Icon = "I", Color = "#000000", CategoryKeys = ["category.test"])]
public class TestImageNode : ITreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Images";

    [NodeProperty("test.image_node.images", Order = 1, Editor = NodePropertyEditor.ImageList)]
    public string ImagesJson { get; set; } = "";
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
    public static ILocService CreateLocService() => new KeyLocService();

    private sealed class KeyLocService : ILocService
    {
        public string CurrentCulture => "en-US";
        public IReadOnlyList<string> SupportedCultures => ["en-US"];
        public string this[string key] => key;
        public string Format(string key, params object[] args) => key;
        public string Format(string key, params (string name, object value)[] args) => key;
        public void SetCulture(string culture) { }
    }

    /// <summary>不提供任何属性选项提供者的插件加载器，避免编辑器测试依赖真实插件扫描。</summary>
    public static IPluginLoaderService CreatePluginLoader(params INodePropertyOptionsProvider[] providers)
    {
        var loader = new Mock<IPluginLoaderService>();
        loader.Setup(l => l.LoadImplementations<INodePropertyOptionsProvider>())
            .Returns(providers.ToList());
        return loader.Object;
    }
}
