using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render
{
    [NodeInfo("node.render_call_preset", Icon = "↗", Color = "var(--node-link)",
        CategoryKeys = ["category.flow", "category.link"],
        DescriptionKey = "node.render_call_preset.desc")]
    public class RenderCallNode : IRenderModifierNode
    {
        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint",
            OptionsKey = "render_modifier_presets",
            OptionsDefaultLabelKey = "node_editor.option_none")]
        public string PresetId { get; set; } = "";

        public void Execute(RenderNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, "Preset ID is empty", context.CurrentNodeId, context.CurrentNodeName);

            var json = context.Env.KVData.Read("RenderModifierManager", "render_modifier_presets");
            if (string.IsNullOrEmpty(json))
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, "No render modifier presets found", context.CurrentNodeId, context.CurrentNodeName);

            var presets = JsonConvert.DeserializeObject<List<RenderModifierPreset>>(json) ?? new();
            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                throw new RenderNodeException(
                    NodeErrorCodes.PresetNotFound, $"Render preset not found: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, $"Render preset has empty tree: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

            var node = context.Env.Serializer.Deserialize(preset.RootNodeJson) as RenderModifierNodeWrapper;
            if (node == null)
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, $"Failed to deserialize render preset: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

            node.Execute(context);
        }
    }
}
