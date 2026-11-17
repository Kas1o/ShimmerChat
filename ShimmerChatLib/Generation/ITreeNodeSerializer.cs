namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点树序列化器的非泛型接口。所有管线（Pre/Post/Render）的序列化器均实现此接口，
    /// 使 UI 组件无需关心具体节点类型即可序列化/反序列化节点树。
    /// </summary>
    public interface ITreeNodeSerializer
    {
        /// <summary>将节点树（包装器树）序列化为 JSON</summary>
        string Serialize(ITreeNode root);

        /// <summary>
        /// 从 JSON 反序列化节点树，失败时返回 null。
        /// 需要显示失败原因时请使用 <see cref="DeserializeTree"/>；本方法只用于错误已由调用方另行处理的场景。
        /// </summary>
        ITreeNode? Deserialize(string json);

        /// <summary>
        /// 反序列化并返回显式结果（成功 / 失败 / 旧格式迁移）。
        /// 失败时不会静默返回空树，调用方必须展示 <see cref="NodeDeserializationResult.Error"/>。
        /// </summary>
        NodeDeserializationResult DeserializeTree(string json);

        /// <summary>获取序列化器已知的所有节点类型（用于 SerializationBinder 白名单）</summary>
        IReadOnlyDictionary<string, Type> GetKnownTypes();
    }
}
