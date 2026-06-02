namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentConfig
    {
        public string Name { get; set; } = "";
        public Guid Guid { get; set; } = Guid.NewGuid();
        public int SelectedApiIndex { get; set; } = -1;
        public List<string> EnabledToolNames { get; set; } = [];
        public string ModifierPresetId { get; set; } = "";
        public string OutputMode { get; set; } = "LastMessage";
    }
}
