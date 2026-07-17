using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    [NodeInfo("node.post_call_preset", Icon = "↗", Color = "var(--node-link)",
        CategoryKeys = ["category.flow", "category.link"],
        DescriptionKey = "node.post_call_preset.desc")]
    public class PostCallNode : IPostGenerationNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Call Preset";

        [NodeProperty("prop.call_node.preset_id", HintKey = "prop.call_node.preset_id.hint")]
        public string PresetId { get; set; } = "";

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(PresetId))
                return Fail(NodeErrorCodes.DataMissing, "Preset ID is empty");

            var kvData = context.Env.Persistent.KVData;
            var json = kvData.Read("PostGenerationManager", "post_generation_presets");
            if (string.IsNullOrEmpty(json))
                return Fail(NodeErrorCodes.DataMissing, "No post-generation presets found");

            var presets = JsonConvert.DeserializeObject<List<PostGenerationPreset>>(json) ?? new();
            var preset = presets.FirstOrDefault(p => p.Id == PresetId);
            if (preset == null)
                return Fail(NodeErrorCodes.PresetNotFound, $"Post-generation preset not found: {PresetId}");

            if (string.IsNullOrWhiteSpace(preset.RootNodeJson))
                return Fail(NodeErrorCodes.DataMissing, $"Post-generation preset has empty tree: {PresetId}");

            var node = context.Env.Serializer.Deserialize(preset.RootNodeJson);
            if (node is not IPostGenerationNode childNode)
                return Fail(NodeErrorCodes.DataMissing, $"Failed to deserialize post-generation preset: {PresetId}");

            var childResult = await childNode.ExecuteAsync(context);
            if (!childResult.Success)
            {
                childResult.NodeId ??= Id;
                childResult.NodeName ??= Name;
            }
            return childResult;
        }

        private PostNodeResult Fail(string code, string message)
        {
            var r = PostNodeResult.Failure(code, message);
            r.NodeId = Id;
            r.NodeName = Name;
            return r;
        }
    }
}
