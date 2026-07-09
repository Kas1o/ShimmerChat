using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 从 KVData 中按名称加载 ToolManager 预设，实例化工具加入 Tools 列表
    /// </summary>
    [NodeInfo("Tool Preset", Icon = "📦", Color = "#40c0a0", Category = "Tool/Preset", Description = "Load a tool preset from ToolManager")]
    public class ToolPresetNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Tool Preset";

        [NodeProperty("Preset Name", Hint = "Name of the tool preset to load from ToolManager")]
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
            if (preset == null || preset.EnabledToolTypeNames.Count == 0)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.PresetNotFound,
                    $"ToolPreset: Preset '{PresetName}' is empty or invalid.",
                    nodeId: Id, nodeName: Name));

            foreach (var typeName in preset.EnabledToolTypeNames)
            {
                var toolType = ResolveToolType(typeName);
                if (toolType == null)
                    continue;
                var ctor = toolType.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                    continue;
                var tool = (IToolV2)Activator.CreateInstance(toolType)!;
                context.Env.Transient.Tools.Add(tool);
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private static Type? ResolveToolType(string toolName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    foreach (var t in asm.GetExportedTypes())
                    {
                        if (!typeof(IToolV2).IsAssignableFrom(t) || t.IsAbstract || t.IsInterface)
                            continue;
                        if (t.GetConstructor(Type.EmptyTypes) == null)
                            continue;
                        try
                        {
                            var instance = (IToolV2)Activator.CreateInstance(t)!;
                            if (instance.Name == toolName)
                                return t;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return null;
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
