using System.Collections.Concurrent;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render
{
    [NodeInfo("node.render_call_preset", Icon = "↗", Color = "var(--node-link)",
        CategoryKeys = ["category.flow", "category.link"],
        DescriptionKey = "node.render_call_preset.desc")]
    public class RenderCallNode : IRenderModifierNode
    {
        /// <summary>
        /// 已反序列化的预设树缓存。预设 JSON 是内容寻址的键，预设被编辑后自然换 key；
        /// 有上限且按先进先出淘汰，避免长期持有旧树。
        /// </summary>
        private static readonly ConcurrentDictionary<PresetCacheKey, RenderModifierNodeWrapper> DeserializedPresets = new();
        private static readonly ConcurrentQueue<PresetCacheKey> PresetOrder = new();
        private const int MaxCachedPresets = 32;

        /// <summary>
        /// 预设列表缓存。列表读取本身也是一次 KV 查询，因此按
        /// <see cref="PipelinePresetStore.GetRevision"/> 缓存；任何经
        /// PipelinePresetStore 的写入都会让版本变化并触发重读。
        /// </summary>
        private static readonly object PresetListSync = new();
        private static long _presetListRevision = -1;
        private static Dictionary<string, RenderModifierPreset>? _presetList;

        private readonly record struct PresetCacheKey(string PresetId, string RootNodeJson);

        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint",
            OptionsKey = "render_modifier_presets",
            OptionsDefaultLabelKey = "node_editor.option_none")]
        public string PresetId { get; set; } = "";

        public void Execute(RenderNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, "Preset ID is empty", context.CurrentNodeId, context.CurrentNodeName);

            var presets = GetPresets(context);
            if (presets.Count == 0)
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, "No render modifier presets found", context.CurrentNodeId, context.CurrentNodeName);

            if (!presets.TryGetValue(PresetId, out var preset))
                throw new RenderNodeException(
                    NodeErrorCodes.PresetNotFound, $"Render preset not found: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                throw new RenderNodeException(
                    NodeErrorCodes.DataMissing, $"Render preset has empty tree: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

            var key = new PresetCacheKey(PresetId, preset.RootNodeJson);
            if (!DeserializedPresets.TryGetValue(key, out var node))
            {
                node = context.Env.Serializer.Deserialize(preset.RootNodeJson) as RenderModifierNodeWrapper;
                if (node == null)
                    throw new RenderNodeException(
                        NodeErrorCodes.DataMissing, $"Failed to deserialize render preset: {PresetId}", context.CurrentNodeId, context.CurrentNodeName);

                CachePreset(key, node);
            }

            node.Execute(context);
        }

        private static Dictionary<string, RenderModifierPreset> GetPresets(RenderNodeExecutionContext context)
        {
            var revision = PipelinePresetStore.GetRevision(GenerationPipeline.RenderModifier);

            lock (PresetListSync)
            {
                if (_presetList == null || _presetListRevision != revision)
                {
                    var presets = PipelinePresetStore.ReadAll<RenderModifierPreset>(
                        context.Env.KVData, GenerationPipeline.RenderModifier);

                    _presetList = presets
                        .GroupBy(p => p.Id, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    _presetListRevision = revision;
                }

                return _presetList;
            }
        }

        private static void CachePreset(PresetCacheKey key, RenderModifierNodeWrapper node)
        {
            if (!DeserializedPresets.TryAdd(key, node)) return;

            PresetOrder.Enqueue(key);
            while (DeserializedPresets.Count > MaxCachedPresets
                && PresetOrder.TryDequeue(out var oldest))
            {
                DeserializedPresets.TryRemove(oldest, out _);
            }
        }

        internal static void ClearCacheForTests()
        {
            DeserializedPresets.Clear();
            while (PresetOrder.TryDequeue(out _)) { }

            lock (PresetListSync)
            {
                _presetList = null;
                _presetListRevision = -1;
            }
        }
    }
}
