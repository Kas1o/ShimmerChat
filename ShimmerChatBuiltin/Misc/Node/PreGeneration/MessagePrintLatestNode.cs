using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    [NodeInfo("node.message_print_latest", Icon = "📄", Color = "var(--node-debug)", CategoryKeys = ["category.debug"], DescriptionKey = "node.message_print_latest.desc")]
    public class MessagePrintLatestNode : IPreGenerationNode
    {

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var output = context.Env.Persistent.DebugOutput;
            var fragments = context.Env.Transient.Fragments;
            if (fragments.Count > 0)
                output.Write(nameof(MessagePrintLatestNode), "info", fragments[^1].Message.Content ?? "");
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
