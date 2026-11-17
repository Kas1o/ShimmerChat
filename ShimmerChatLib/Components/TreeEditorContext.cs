using System.Reflection;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Components;

/// <summary>
/// 节点树编辑器的上下文，通过 CascadingValue 向下传递给所有子组件。
/// 封装了当前管线的序列化器、类型目录和节点接口类型。
/// 每个管线页面（Pre/Post/Render）创建各自的 TreeEditorContext。
/// </summary>
public class TreeEditorContext
{
    /// <summary>当前管线的节点树序列化器</summary>
    public ITreeNodeSerializer Serializer { get; }

    /// <summary>节点类型目录</summary>
    public INodeTypeCatalog TypeCatalog { get; }

    /// <summary>当前管线的节点接口类型（typeof(IPreGenerationNode) 等）</summary>
    public Type NodeInterfaceType { get; }

    /// <summary>当前管线的包装器类型</summary>
    public Type WrapperType { get; }

    /// <summary>剪贴板 JSON 缓存</summary>
    private string? _clipboardJson;

    public bool HasClipboardContent => _clipboardJson != null;

    /// <summary>剪贴板内容变化时触发，供各层 ChildListEditor / SlotEditor 刷新粘贴按钮可见性</summary>
    public event Action? ClipboardChanged;

    public TreeEditorContext(ITreeNodeSerializer serializer, INodeTypeCatalog typeCatalog, Type nodeInterfaceType, Type? wrapperType = null)
    {
        Serializer = serializer;
        TypeCatalog = typeCatalog;
        NodeInterfaceType = nodeInterfaceType;
        WrapperType = wrapperType ?? NodeWrapperTypes.Get(nodeInterfaceType);
    }

    /// <summary>获取当前管线可用的所有节点类型</summary>
    public IReadOnlyList<NodeTypeMetadata> GetAvailableNodeTypes()
        => TypeCatalog.GetNodeTypes(NodeInterfaceType);

    /// <summary>创建包装器 + 业务节点实例，显示名取 NodeInfo 标签 Key。</summary>
    public ITreeNode CreateNode(Type type)
    {
        var payload = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Failed to create node instance of '{type.FullName}'.");

        return CreateWrapper(payload, NodeNaming.GetDefaultName(type));
    }

    /// <summary>用当前管线对应的包装器类型包装业务节点。</summary>
    public ITreeNode CreateWrapper(object payload, string? name = null)
    {
        var wrapper = Activator.CreateInstance(WrapperType)
            ?? throw new InvalidOperationException($"Failed to create node wrapper '{WrapperType.FullName}'.");

        var nodeProperty = WrapperType.GetProperty(nameof(NodeWrapper<object>.Node), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Wrapper type '{WrapperType.FullName}' has no Node property.");

        nodeProperty.SetValue(wrapper, payload);
        var treeNode = (ITreeNode)wrapper;
        treeNode.Name = name ?? NodeNaming.GetDefaultName(payload.GetType());
        return treeNode;
    }

    /// <summary>复制节点到剪贴板</summary>
    public void Copy(ITreeNode node)
    {
        _clipboardJson = Serializer.Serialize(node);
        ClipboardChanged?.Invoke();
    }

    /// <summary>从剪贴板粘贴节点（自动重新生成所有 Id）</summary>
    public ITreeNode? Paste()
    {
        if (_clipboardJson == null) return null;
        var node = Serializer.Deserialize(_clipboardJson);
        if (node != null) TreeNodeReflection.RegenerateIds(node);
        return node;
    }

    /// <summary>清空剪贴板</summary>
    public void ClearClipboard()
    {
        _clipboardJson = null;
    }
}
