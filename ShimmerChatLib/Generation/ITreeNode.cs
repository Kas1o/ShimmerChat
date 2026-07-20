namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点树中的最小节点接口。所有管线节点（Pre/Post/Render）均实现此接口，
    /// 使节点编辑器 UI 组件可以统一操作不同类型的节点树。
    /// </summary>
    public interface ITreeNode
    {
        /// <summary>
        /// 节点唯一标识（GUID）
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 节点显示名称，用户可编辑
        /// </summary>
        string Name { get; set; }
    }
}
