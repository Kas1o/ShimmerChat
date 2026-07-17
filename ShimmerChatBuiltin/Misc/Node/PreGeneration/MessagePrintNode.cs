using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    [NodeInfo("node.message_print", Icon = "🖨️", Color = "var(--node-debug)", CategoryKeys = ["category.debug"], DescriptionKey = "node.message_print.desc")]
    public class MessagePrintNode : IPreGenerationNode
    {
        private static readonly JsonSerializerSettings _settings = new()
        {
            Converters = { new StringEnumConverter() }
        };

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Message Print";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var output = context.Env.Persistent.DebugOutput;
            var messages = context.Env.Transient.Fragments
                .Select(s => (s.Message, s.From))
                .ToArray();
            var json = JsonConvert.SerializeObject(messages, _settings);
            output.Write(nameof(MessagePrintNode), "info", json);
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
