namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentConfig
    {
        public string Name { get; set; } = "";
        public Guid Guid { get; set; } = Guid.NewGuid();
        public bool UseSharedPreset { get; set; } = true;
        public string ModifierPresetId { get; set; } = "";
        public string OutputMode { get; set; } = "LastMessage";

        /// <summary>
        /// 私有修饰器树的 JSON 序列化。
        /// 非空时优先于 ModifierPresetId，对标 Agent.ModifierTreeJson。
        /// </summary>
        public string? ModifierTreeJson { get; set; }
    }
}
