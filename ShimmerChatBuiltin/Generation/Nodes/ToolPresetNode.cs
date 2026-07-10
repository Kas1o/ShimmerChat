using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 从 KVData 中按名称加载 ToolManager 预设，通过 IAutoCreateToolV2.Create 实例化工具加入 Tools 列表
    /// </summary>
    [NodeInfo("node.tool_preset", Icon = "📦", Color = "#40c0a0", CategoryKeys = ["category.tool", "category.preset"], DescriptionKey = "node.tool_preset.desc")]
    public class ToolPresetNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Tool Preset";

        [NodeProperty("prop.tool_preset.preset_name", HintKey = "prop.tool_preset.preset_name.hint")]
        public string PresetName { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetName))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    "ToolPreset: PresetName is empty.",
                    nodeId: Id, nodeName: Name));

            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("ToolPresets", PresetName);
            if (string.IsNullOrEmpty(json))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    $"ToolPreset: Preset '{PresetName}' not found in KVData.",
                    nodeId: Id, nodeName: Name));

            var preset = JsonConvert.DeserializeObject<ToolPresetData>(json);
            if (preset == null)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    $"ToolPreset: Preset '{PresetName}' is invalid.",
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

        /// <summary>工具预设数据（与 ToolManager 共用结构）</summary>
        public class ToolPresetData
        {
            public string Name { get; set; } = "";
            public List<string> EnabledToolTypeNames { get; set; } = new();
        }
    }

    /// <summary>工具预设（供 ToolManager 等使用）</summary>
    public class ToolPreset
    {
        public string Name { get; set; } = "";
        public List<string> EnabledToolTypeNames { get; set; } = new();
    }
}
