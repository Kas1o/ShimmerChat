using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.message_print", Icon = "🖨️", Color = "#888", CategoryKeys = ["category.debug"], DescriptionKey = "node.message_print.desc")]
    public class MessagePrintNode : IGenerationNode
    {
        private static readonly JsonSerializerSettings _settings = new()
        {
            Converters = { new StringEnumConverter() }
        };

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Message Print";

        public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var messages = context.Env.Transient.Fragments
                .Select(s => (s.Message, s.From))
                .ToArray();
            var json = JsonConvert.SerializeObject(messages, _settings);
            Console.WriteLine(json);
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
