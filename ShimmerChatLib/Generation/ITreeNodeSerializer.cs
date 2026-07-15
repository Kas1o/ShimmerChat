namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点树序列化器的非泛型接口。所有管线（Pre/Post/Render）的序列化器均实现此接口，
    /// 使 UI 组件无需关心具体节点类型即可序列化/反序列化节点树。
    /// </summary>
    public interface ITreeNodeSerializer
    {
        /// <summary>将节点树序列化为 JSON</summary>
        string Serialize(ITreeNode root);

        /// <summary>从 JSON 反序列化节点树，失败时返回 null</summary>
        ITreeNode? Deserialize(string json);

        /// <summary>获取序列化器已知的所有节点类型（用于 SerializationBinder 白名单）</summary>
        IReadOnlyDictionary<string, Type> GetKnownTypes();
    }
}
