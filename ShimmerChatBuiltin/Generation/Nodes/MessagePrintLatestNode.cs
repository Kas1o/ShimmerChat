using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("Message Print Latest", Icon = "📄", Color = "#888", Category = "Debug", Description = "Print the latest fragment's message content to console")]
    public class MessagePrintLatestNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Message Print Latest";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;
            if (fragments.Count > 0)
                Console.WriteLine(fragments[^1].Message.Content);
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
