using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    [NodeInfo("node.post_call_preset", Icon = "↗", Color = "var(--node-link)",
        CategoryKeys = ["category.flow", "category.link"],
        DescriptionKey = "node.post_call_preset.desc")]
    public class PostCallNode : IPostGenerationNode
    {
        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint",
            OptionsKey = "post_generation_presets",
            OptionsDefaultLabelKey = "node_editor.option_none")]
        public string PresetId { get; set; } = "";

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                return Fail(NodeErrorCodes.DataMissing, "Preset ID is empty");

            var kvData = context.Env.Persistent.KVData;
            var presets = PipelinePresetStore.ReadAll<PostGenerationPreset>(kvData, GenerationPipeline.PostGeneration);
            if (presets.Count == 0)
                return Fail(NodeErrorCodes.DataMissing, "No post-generation presets found");

            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                return Fail(NodeErrorCodes.PresetNotFound, $"Post-generation preset not found: {PresetId}");

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return Fail(NodeErrorCodes.DataMissing, $"Post-generation preset has empty tree: {PresetId}");

            var node = context.Env.Serializer.Deserialize(preset.RootNodeJson) as PostGenerationNodeWrapper;
            if (node == null)
                return Fail(NodeErrorCodes.DataMissing, $"Failed to deserialize post-generation preset: {PresetId}");

            return await node.ExecuteAsync(context);
        }

        private PostNodeResult Fail(string code, string message)
            => PostNodeResult.Failure(code, message);
    }
}
