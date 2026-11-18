namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点树中的树节点（包装器）。树结构相关状态（Id、显示名、展开状态、旁路状态）由包装器持有，
    /// 业务节点只实现管线接口（<see cref="IPreGenerationNode"/> 等），不再重复声明这些字段。
    /// </summary>
    public interface ITreeNode
    {
        /// <summary>节点唯一标识（GUID），用于 Blazor @key、拖拽环路检测与错误定位。</summary>
        string Id { get; set; }

        /// <summary>节点显示名称，用户可编辑。</summary>
        string Name { get; set; }

        /// <summary>节点在编辑器中的展开状态，随节点树一起持久化。</summary>
        bool IsExpanded { get; set; }

        /// <summary>
        /// 旁路开关：开启后执行阶段跳过该节点及其整棵子树（等价于临时摘除，配置原样保留）。
        /// 随节点树一起持久化；根包装器也可旁路，效果是整条管线空跑。
        /// </summary>
        bool IsBypassed { get; set; }

        /// <summary>业务节点实例。反序列化失败时可能为 null。</summary>
        object? Payload { get; }

        /// <summary>业务节点的实际类型。</summary>
        Type PayloadType { get; }
    }
}
