using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 根据 PresetId 从 KVData 加载预设并内联执行其根节点
    /// </summary>
    [NodeInfo("Call Preset", Icon = "↗", Color = "#40c0a0", Category = "Flow/Link", Description = "Load and execute a preset from Generation Manager")]
    public class CallNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Call Preset";

        [NodeProperty("Preset ID", Hint = "ID of the generation preset to inline")]
        public string PresetId { get; set; } = "";

        public async Task ExecuteAsync(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                return;

            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("GenerationManager", "generation_presets");
            if (string.IsNullOrEmpty(json))
                return;

            var presets = JsonConvert.DeserializeObject<List<GenerationPreset>>(json ?? "[]") ?? new();
            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null || string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return;

            var node = GenerationNodeSerializer.Deserialize(preset.RootNodeJson);
            if (node != null)
                await node.ExecuteAsync(context);
        }
    }
}
