using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.post_debug_output",
        Icon = "🐛",
        Color = "var(--node-debug)",
        CategoryKeys = ["category.debug"],
        DescriptionKey = "node.post_debug_output.desc")]
    public class PostDebugOutputNode : IPostGenerationNode
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Debug Output";

        [NodeProperty("prop.debug_output.source", Order = 10,
            HintKey = "prop.debug_output.source_hint")]
        public string Source { get; set; } = "PostGeneration";

        [NodeProperty("prop.debug_output.category", Order = 20,
            HintKey = "prop.debug_output.category_hint")]
        public string Category { get; set; } = "RawResponse";

        public Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            context.Env.Persistent.DebugOutput.Write(Source, Category, JsonConvert.SerializeObject(context.Env.ResponseMessage));
            return Task.FromResult(PostNodeResult.SuccessResult());
        }
    }
}
