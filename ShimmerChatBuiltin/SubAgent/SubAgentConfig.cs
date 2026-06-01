using ShimmerChatLib.Context;

namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentConfig
    {
        public string Name { get; set; } = "";
        public Guid Guid { get; set; } = Guid.NewGuid();
        public int SelectedApiIndex { get; set; } = -1;
        public List<string> EnabledToolNames { get; set; } = [];
        public List<SubAgentModifierConfig> EnabledModifiers { get; set; } = [];
        public string OutputMode { get; set; } = "LastMessage";
    }

    public class SubAgentModifierConfig
    {
        public string Name { get; set; } = "";
        public ModifierConfig Config { get; set; } = new LegacyModifierConfig { Value = "" };

        public string Input
        {
            get => Config is LegacyModifierConfig l ? l.Value : Config.GetType().Name;
            set => Config = new LegacyModifierConfig { Value = value };
        }
    }
}
