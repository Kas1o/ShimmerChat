using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    /// <summary>
    /// 根据 PresetId 从 KVData 加载预设并内联执行其根节点
    /// </summary>
    [NodeInfo("node.call_preset", Icon = "↗", Color = "var(--node-link)", CategoryKeys = ["category.flow", "category.link"], DescriptionKey = "node.call_preset.desc")]
    [NodeEditor(typeof(CallNodeEditor))]
    public class CallNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Call Preset";

        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint")]
        public string PresetId { get; set; } = "";

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(PresetId))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.call_empty_id"],
                    nodeId: Id, nodeName: Name);

            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("GenerationManager", "generation_presets");
            if (string.IsNullOrEmpty(json))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.call_no_presets"],
                    nodeId: Id, nodeName: Name);

            var presets = JsonConvert.DeserializeObject<List<PreGenerationPreset>>(json) ?? new();
            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                return NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    loc.Format("node_err.call_preset_not_found", PresetId),
                    nodeId: Id, nodeName: Name);

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc.Format("node_err.call_empty_json", PresetId),
                    nodeId: Id, nodeName: Name);

            var node = context.Env.Persistent.Serializer.Deserialize(preset.RootNodeJson);
            if (node == null)
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc.Format("node_err.call_deserialize_failed", PresetId),
                    nodeId: Id, nodeName: Name);

            var childResult = await node.ExecuteAsync(context);
            if (!childResult.Success)
            {
                childResult.NodeId ??= Id;
                childResult.NodeName ??= Name;
            }
            return childResult;
        }
    }
}
