namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 预生成预设：一个可复用的节点树模板
    /// </summary>
    public class PreGenerationPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";
    }

    /// <summary>
    /// 后生成预设：一个可复用的后生成节点树模板
    /// </summary>
    public class PostGenerationPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";
    }

    /// <summary>
    /// 渲染修改预设：一个可复用的渲染修改节点树模板
    /// </summary>
    public class RenderModifierPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";
    }
}
