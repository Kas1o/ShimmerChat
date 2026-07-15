using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.render_call_preset", Icon = "↗", Color = "var(--node-link)",
        CategoryKeys = ["category.flow", "category.link"],
        DescriptionKey = "node.render_call_preset.desc")]
    public class RenderCallNode : IRenderModifierNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        [NodeProperty("prop.node.name", Order = -100)]
        public string Name { get; set; } = "Call Preset";

        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint")]
        public string PresetId { get; set; } = "";

        public async Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                return RenderNodeResult.Failure(
                    NodeErrorCodes.DataMissing, "Preset ID is empty", Id, Name);

            var json = context.Env.KVData.Read("RenderModifierManager", "render_modifier_presets");
            if (string.IsNullOrEmpty(json))
                return RenderNodeResult.Failure(
                    NodeErrorCodes.DataMissing, "No render modifier presets found", Id, Name);

            var presets = JsonConvert.DeserializeObject<List<RenderModifierPreset>>(json) ?? new();
            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                return RenderNodeResult.Failure(
                    NodeErrorCodes.PresetNotFound, $"Render preset not found: {PresetId}", Id, Name);

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return RenderNodeResult.Failure(
                    NodeErrorCodes.DataMissing, $"Render preset has empty tree: {PresetId}", Id, Name);

            var node = context.Env.Serializer.Deserialize(preset.RootNodeJson);
            if (node is not IRenderModifierNode childNode)
                return RenderNodeResult.Failure(
                    NodeErrorCodes.DataMissing, $"Failed to deserialize render preset: {PresetId}", Id, Name);

            var childResult = await childNode.ExecuteAsync(context);
            if (!childResult.Success)
            {
                childResult.NodeId ??= Id;
                childResult.NodeName ??= Name;
            }
            return childResult;
        }
    }
}
