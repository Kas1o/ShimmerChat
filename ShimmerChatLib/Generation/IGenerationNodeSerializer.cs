namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 预生成节点树序列化器。继承 <see cref="ITreeNodeSerializer"/>，添加类型安全的便捷方法。
    /// </summary>
    public interface IPreGenerationNodeSerializer : ITreeNodeSerializer
    {
        /// <summary>类型安全的序列化重载</summary>
        string Serialize(PreGenerationNodeWrapper root);

        /// <summary>类型安全的反序列化重载</summary>
        new PreGenerationNodeWrapper? Deserialize(string json);
    }

    /// <summary>
    /// 后生成节点树序列化器。继承 <see cref="ITreeNodeSerializer"/>，添加类型安全的便捷方法。
    /// </summary>
    public interface IPostGenerationNodeSerializerService : ITreeNodeSerializer
    {
        /// <summary>类型安全的序列化重载</summary>
        string Serialize(PostGenerationNodeWrapper root);

        /// <summary>类型安全的反序列化重载</summary>
        new PostGenerationNodeWrapper? Deserialize(string json);
    }

    /// <summary>
    /// 渲染修改节点树序列化器。继承 <see cref="ITreeNodeSerializer"/>，添加类型安全的便捷方法。
    /// 与 Pre/Post 两个接口保持同一形态，使按管线分派的代码（如 Creator 工具集）
    /// 能以同一方式解析三种序列化器。
    /// </summary>
    public interface IRenderModifierNodeSerializer : ITreeNodeSerializer
    {
        /// <summary>类型安全的序列化重载</summary>
        string Serialize(RenderModifierNodeWrapper root);

        /// <summary>类型安全的反序列化重载</summary>
        new RenderModifierNodeWrapper? Deserialize(string json);
    }
}
