using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    /// <summary>
    /// 根据 PresetId 从 KVData 加载预设并内联执行其根节点
    /// </summary>
    [NodeInfo("node.call_preset", Icon = "↗", Color = "var(--node-link)", CategoryKeys = ["category.flow", "category.link"], DescriptionKey = "node.call_preset.desc")]
    public class CallNode : IPreGenerationNode
    {

        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint",
            OptionsKey = "pre_generation_presets", OptionsSearchable = true,
            OptionsDefaultLabelKey = "node_editor.option_none")]
        public string PresetId { get; set; } = "";

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(PresetId))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.call_empty_id"]);

            var kvData = context.Env.Persistent.KVData;
            var presets = PipelinePresetStore.ReadAll<PreGenerationPreset>(kvData, GenerationPipeline.PreGeneration);
            if (presets.Count == 0)
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.call_no_presets"]);

            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                return NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    loc.Format("node_err.call_preset_not_found", PresetId));

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc.Format("node_err.call_empty_json", PresetId));

            var node = context.Env.Persistent.Serializer.Deserialize(preset.RootNodeJson);
            if (node == null)
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc.Format("node_err.call_deserialize_failed", PresetId));

            return await node.ExecuteAsync(context);
        }
    }
}
