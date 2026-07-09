namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 迁移自旧 IContextModifierService。SubAgent 独立配置时使用。
    /// </summary>
    public class ActivatedModifier
    {
        public required string Name { get; set; }
        public string? ConfigJson { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
