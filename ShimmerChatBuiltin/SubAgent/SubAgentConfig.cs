using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentConfig
    {
        public string Name { get; set; } = "";
        public Guid Guid { get; set; } = Guid.NewGuid();
        public int SelectedApiIndex { get; set; } = -1;
        public List<string> EnabledToolNames { get; set; } = [];
        public bool UseSharedPreset { get; set; } = true;
        public string ModifierPresetId { get; set; } = "";
        public List<ActivatedModifier> IndependentModifiers { get; set; } = new();
        public string OutputMode { get; set; } = "LastMessage";
    }
}
