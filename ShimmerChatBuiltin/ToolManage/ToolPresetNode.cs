using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.ToolManage
{
    /// <summary>
    /// 从 KVData 中加载工具预设。PresetName 为空时使用 IsDefault 预设，否则按名称匹配。
    /// </summary>
    [NodeInfo("node.tool_preset", Icon = "📦", Color = "var(--node-link)", CategoryKeys = ["category.tool", "category.preset"], DescriptionKey = "node.tool_preset.desc")]
    public class ToolPresetNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Tool Preset";

        [NodeProperty("prop.tool_preset.preset_name", HintKey = "prop.tool_preset.preset_name.hint")]
        public string PresetName { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("ToolPresets", "__presets__");
            if (string.IsNullOrEmpty(json))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    loc["node_err.tool_preset_none"],
                    nodeId: Id, nodeName: Name));

            var presets = JsonConvert.DeserializeObject<List<ToolPreset>>(json);
            if (presets == null || presets.Count == 0)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    loc["node_err.tool_preset_empty"],
                    nodeId: Id, nodeName: Name));

            ToolPreset? preset;
            if (string.IsNullOrWhiteSpace(PresetName))
                preset = presets.FirstOrDefault(p => p.IsDefault);
            else
                preset = presets.FirstOrDefault(p => p.Name == PresetName);

            if (preset == null)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    loc.Format("node_err.tool_preset_not_found", string.IsNullOrWhiteSpace(PresetName) ? "(default)" : PresetName),
                    nodeId: Id, nodeName: Name));

            foreach (var typeName in preset.EnabledToolTypeNames)
            {
                var meta = context.Env.Persistent.ToolRegistry.FindByName(typeName);
                if (meta == null) continue;

                var tool = context.Env.Persistent.ToolRegistry.CreateInstance(meta.Type, context.Env.Persistent);
                if (tool != null)
                    context.Env.Transient.Tools.Add(tool);
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }

    /// <summary>工具预设</summary>
    public class ToolPreset
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; } = false;
        public List<string> EnabledToolTypeNames { get; set; } = new();
    }
}
