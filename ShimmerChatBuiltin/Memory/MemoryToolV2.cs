using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using static Qdrant.Client.Grpc.Conditions;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// IAutoCreateToolV2 版本的 MemoryTool。
    /// </summary>
    public class MemoryToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly Guid _agentGuid;

        public static string NameKey => "tool.memory";
        public static string DescriptionKey => "tool.memory.desc";
        public static string[] CategoryKeys => ["category.memory"];

        public MemoryToolV2() { }

        private MemoryToolV2(IKVDataService kvData, Guid agentGuid)
        {
            _kvData = kvData;
            _agentGuid = agentGuid;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new MemoryToolV2(env.KVData, env.AgentGuid);

        public Tool GetDefinition() => new()
        {
            name = "MemoryTool",
            description = "A tool for managing memory in conversations. Supports add, delete, and search with similarity or keyword mode.",
            parameters =
            [
                (new ToolParameter { name = "action", type = ParameterType.String, description = "Action: add, delete, or search.", @enum = ["add", "delete", "search"] }, true),
                (new ToolParameter { name = "content", type = ParameterType.String, description = "Content to add or search for." }, false),
                (new ToolParameter { name = "id", type = ParameterType.String, description = "The ID of the memory to delete." }, false),
                (new ToolParameter { name = "search_mode", type = ParameterType.String, description = "Search mode: similarity or keyword.", @enum = ["similarity", "keyword"] }, false)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            var action = JsonConvert.DeserializeObject<InputAction>(input);

            var qHost = _kvData.Read("QdrantAPI", "host") ?? throw new InvalidDataException("Qdrant API host not set.");
            var qPort = _kvData.Read("QdrantAPI", "port") ?? throw new InvalidDataException("Qdrant API port not set.");
            var qApiKey = _kvData.Read("QdrantAPI", "apikey");
            var vectorSize = ulong.Parse(_kvData.Read("QdrantAPI", "dim") ?? "1024");

            var qclient = new QdrantClient(qHost, int.Parse(qPort));
            if (!string.IsNullOrEmpty(qApiKey))
            {
                var chnl = QdrantChannel.ForAddress(new Uri($"http://{qHost}:{qPort}"), new ClientConfiguration { ApiKey = qApiKey });
                qclient = new QdrantClient(new QdrantGrpcClient(chnl));
            }

            var eUri = _kvData.Read("EmbeddingAPI", "uri") ?? string.Empty;
            var eApiKey = _kvData.Read("EmbeddingAPI", "apikey") ?? string.Empty;
            var eModelName = _kvData.Read("EmbeddingAPI", "modelname") ?? string.Empty;
            var api = new OpenAIChatCompletionClient(eUri, eApiKey, eModelName);

            var exists = await qclient.CollectionExistsAsync($"{_agentGuid}");
            if (!exists)
            {
                await qclient.CreateCollectionAsync($"{_agentGuid}",
                    vectorsConfig: new VectorParams { Size = vectorSize, Distance = Distance.Cosine });
            }

            var collectionName = $"{_agentGuid}";

            return action.action switch
            {
                "add" => await HandleAdd(qclient, api, action, collectionName),
                "delete" => await HandleDelete(qclient, action, collectionName),
                "search" => await HandleSearch(qclient, api, action, collectionName),
                _ => "Unknown action."
            };
        }

        private static async Task<string> HandleAdd(QdrantClient qclient, OpenAIChatCompletionClient api, InputAction action, string collectionName)
        {
            if (action.content == null) return "Content cannot be null when adding memory.";
            var embd = await api.GetEmbedding(action.content);
            var point = new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = embd.ToArray(),
                Payload = { ["content"] = action.content, ["timestamp"] = DateTime.UtcNow.ToString("o") }
            };
            await qclient.UpsertAsync(collectionName, points: [point]);
            return $"Memory added: {action.content}";
        }

        private static async Task<string> HandleDelete(QdrantClient qclient, InputAction action, string collectionName)
        {
            if (action.id == null) return "ID cannot be null when deleting memory.";
            var result = await qclient.DeleteAsync(collectionName, id: Guid.Parse(action.id));
            return $"Memory with ID {action.id} deleted.";
        }

        private static async Task<string> HandleSearch(QdrantClient qclient, OpenAIChatCompletionClient api, InputAction action, string collectionName)
        {
            if (action.content == null) return "Content cannot be null when searching memory.";
            var sb = new System.Text.StringBuilder();

            if (action.search_mode == "keyword")
            {
                var filter = MatchText("content", action.content);
                var result = await qclient.ScrollAsync(collectionName, filter: filter, limit: 5);
                sb.AppendLine("Search Results:");
                foreach (var res in result.Result)
                {
                    if (res.Payload.ContainsKey("content"))
                        sb.AppendLine($"ID: {res.Id}, Content: {res.Payload["content"]}");
                }
            }
            else
            {
                var embd = await api.GetEmbedding(action.content);
                var result = await qclient.SearchAsync(collectionName, vector: embd.ToArray(), limit: 5);
                sb.AppendLine("Search Results:");
                foreach (var res in result)
                {
                    if (res.Payload.ContainsKey("content"))
                        sb.AppendLine($"ID: {res.Id}, Score: {res.Score}, Content: {res.Payload["content"]}");
                }
            }
            return sb.ToString();
        }

        private struct InputAction
        {
            public string action { get; set; }
            public string? content { get; set; }
            public string? id { get; set; }
            public string search_mode { get; set; }
        }
    }
}
