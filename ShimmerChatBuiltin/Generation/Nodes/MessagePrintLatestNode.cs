using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.message_print_latest", Icon = "📄", Color = "var(--node-debug)", CategoryKeys = ["category.debug"], DescriptionKey = "node.message_print_latest.desc")]
    public class MessagePrintLatestNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Message Print Latest";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var output = context.Env.Persistent.DebugOutput;
            var fragments = context.Env.Transient.Fragments;
            if (fragments.Count > 0)
                output.Write(nameof(MessagePrintLatestNode), "info", fragments[^1].Message.Content ?? "");
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
