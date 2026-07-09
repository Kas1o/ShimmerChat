using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// Qdrant 记忆检索注入节点：基于最近的 N 条消息检索相关记忆并注入到上下文
    /// </summary>
    [NodeInfo("Memory Retrieve", Icon = "🔍", Color = "#e0c060", Category = "Content/Memory")]
    public class MemoryRetrieveNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Memory Retrieve";

        /// <summary>
        /// 用于检索的最近消息数量
        /// </summary>
        [NodeProperty("Recent Messages", Hint = "Number of recent messages used for memory search")]
        public int RecentMessageCount { get; set; } = 3;

        /// <summary>
        /// 相似度阈值 (0.0 ~ 1.0)
        /// </summary>
        [NodeProperty("Similarity Threshold", Hint = "Minimum similarity score (0.0 - 1.0)")]
        public float SimilarityThreshold { get; set; } = 0.5f;

        /// <summary>
        /// 注入位置（-1 为开头，-2 为末尾）
        /// </summary>
        [NodeProperty("Insert Position", Hint = "Where to insert memories (0 = start)")]
        public int InsertPosition { get; set; } = 0;

        private static Dictionary<string, List<float>>? _vectorCache;

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            var kvData = context.Env.Persistent.KVData;
            var agentGuid = context.Env.Persistent.AgentGuid;
            var chat = context.Env.Persistent.GetChat();

            var recentMessages = context.Env.Transient.Fragments
                .Where(f => f.From != PromptBuilder.From.tool_result && !string.IsNullOrEmpty(f.Message.Content))
                .Reverse()
                .Take(RecentMessageCount)
                .Reverse()
                .ToList();

            if (recentMessages.Count == 0)
                return NodeResult.SuccessResult();

            var qHost = kvData.Read("QdrantAPI", "host");
            if (string.IsNullOrEmpty(qHost))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    "MemoryRetrieve: Qdrant host not configured.",
                    nodeId: Id, nodeName: Name);
            var qPort = kvData.Read("QdrantAPI", "port");
            if (string.IsNullOrEmpty(qPort))
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    "MemoryRetrieve: Qdrant port not configured.",
                    nodeId: Id, nodeName: Name);
            var qApiKey = kvData.Read("QdrantAPI", "apikey");

            try
            {
                var qclient = new QdrantClient(qHost, int.Parse(qPort));
                if (!string.IsNullOrEmpty(qApiKey))
                {
                    var chnl = QdrantChannel.ForAddress(
                        new Uri($"http://{qHost}:{qPort}"),
                        new ClientConfiguration { ApiKey = qApiKey });
                    qclient = new QdrantClient(new QdrantGrpcClient(chnl));
                }

                var exists = await qclient.CollectionExistsAsync($"{agentGuid}");
                if (!exists)
                    return NodeResult.SuccessResult();

                var eUri = kvData.Read("EmbeddingAPI", "uri") ?? string.Empty;
                var eApiKey = kvData.Read("EmbeddingAPI", "apikey") ?? string.Empty;
                var eModelName = kvData.Read("EmbeddingAPI", "modelname") ?? string.Empty;
                var api = new OpenAIAPI(eUri, eApiKey, eModelName);

                _vectorCache ??= new Dictionary<string, List<float>>();

                var vectors = new List<(List<float> vector, int weight)>();
                int idx = 0;
                foreach (var msg in recentMessages)
                {
                    idx++;
                    if (!_vectorCache.ContainsKey(msg.Message.Content))
                    {
                        var vec = await api.GetEmbedding(msg.Message.Content);
                        _vectorCache[msg.Message.Content] = vec;
                    }
                    vectors.Add((_vectorCache[msg.Message.Content], idx));
                }

                var maxWeight = vectors.Count > 0 ? vectors.Max(v => v.weight) : 1;
                var searchResults = new List<string>();

                foreach (var (vector, weight) in vectors)
                {
                    var actualThreshold = (1f - (0.9f * (weight - 1) / (maxWeight - 1 == 0 ? 1 : maxWeight - 1))) * SimilarityThreshold;
                    var results = await qclient.SearchAsync(
                        collectionName: $"{agentGuid}",
                        vector: vector.ToArray().AsMemory(),
                        limit: 3,
                        scoreThreshold: actualThreshold);

                    foreach (var res in results)
                    {
                        for (int w = 0; w < weight; w++)
                        {
                            if (res.Payload.ContainsKey("content"))
                                searchResults.Add(res.Payload["content"].ToString()!);
                        }
                    }
                }

                var distinct = searchResults.Distinct().ToList();
                if (distinct.Count == 0)
                    return NodeResult.SuccessResult();

                var memoryText = string.Join("\n---\n", distinct);
                var segment = new ContextSegment
                {
                    SourceType = typeof(MemoryRetrieveNode),
                    Message = new ChatMessage { Content = $"System Auto Recall Memories:\n{{\n{memoryText}\n}}" },
                    From = PromptBuilder.From.system,
                    Metadata = new Dictionary<string, object> { ["memoryCount"] = distinct.Count }
                };

                if (InsertPosition < 0 || InsertPosition >= context.Env.Transient.Fragments.Count)
                    context.Env.Transient.Fragments.Add(segment);
                else
                    context.Env.Transient.Fragments.Insert(InsertPosition, segment);

                return NodeResult.SuccessResult();
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ServiceError,
                    $"MemoryRetrieve: Failed to retrieve memories.",
                    details: ex.ToString(),
                    nodeId: Id, nodeName: Name);
            }
        }
    }
}
