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
        /// 私有修饰器树（Pre-Generation）的 JSON 序列化。
        /// 非空时优先于 ModifierPresetId，对标 Agent.PreGenerationTreeJson。
        /// </summary>
        public string? ModifierTreeJson { get; set; }

        /// <summary>
        /// 后生成管线树的 JSON 序列化。
        /// 非空时在 ToolCallLoop 之后执行 Post-Generation 管线处理响应文本。
        /// 对标 Agent.PostGenerationTreeJson。
        /// </summary>
        public string? PostGenerationTreeJson { get; set; }

        /// <summary>收藏标记；只影响子智能体选择器与检索列表的排序。</summary>
        public bool Pinned { get; set; }

        /// <summary>复制一份配置；Guid 由调用方重新分配。</summary>
        public SubAgentConfig Clone() => new()
        {
            Name = Name,
            Guid = Guid.Empty,
            UseSharedPreset = UseSharedPreset,
            ModifierPresetId = ModifierPresetId,
            OutputMode = OutputMode,
            ModifierTreeJson = ModifierTreeJson,
            PostGenerationTreeJson = PostGenerationTreeJson,
            Pinned = Pinned
        };
    }
}
