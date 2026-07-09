namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 生成预设：一个可复用的节点树模板
    /// </summary>
    public class GenerationPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Preset";

        /// <summary>
        /// 根节点的 JSON 序列化
        /// </summary>
        public string RootNodeJson { get; set; } = "{}";
    }
}
