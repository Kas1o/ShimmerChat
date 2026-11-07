using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// 记忆检索注入节点：用最近的对话消息作为查询，从当前模型的记忆索引里召回相关条目。
    /// 索引未就绪时明确失败，不静默返回空结果。
    /// </summary>
    [NodeInfo("node.memory_retrieve", Icon = "🔍", Color = "var(--node-memory)", CategoryKeys = ["category.content", "category.memory_node"])]
    public class MemoryRetrieveNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Memory Retrieve";

        /// <summary>指定使用的嵌入配置；留空表示使用当前激活配置。</summary>
        [NodeProperty("prop.memory_retrieve.profile", HintKey = "prop.memory_retrieve.profile.hint", Order = -1, OptionsKey = "embedding_profiles")]
        public string EmbeddingProfileId { get; set; } = string.Empty;

        /// <summary>用于检索的最近消息数量</summary>
        [NodeProperty("prop.memory_retrieve.recent_messages", HintKey = "prop.memory_retrieve.recent_messages.hint")]
        public int RecentMessageCount { get; set; } = 3;

        /// <summary>相似度阈值 (0.0 ~ 1.0)</summary>
        [NodeProperty("prop.memory_retrieve.similarity_threshold", HintKey = "prop.memory_retrieve.similarity_threshold.hint")]
        public float SimilarityThreshold { get; set; } = 0.5f;

        /// <summary>注入位置（-1 为开头，-2 为末尾）</summary>
        [NodeProperty("prop.memory_retrieve.insert_position", HintKey = "prop.memory_retrieve.insert_position.hint")]
        public int InsertPosition { get; set; } = 0;

        /// <summary>每条查询召回条数</summary>
        [NodeProperty("prop.memory_retrieve.top_k", HintKey = "prop.memory_retrieve.top_k.hint")]
        public int TopK { get; set; } = 3;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;
            var agentGuid = context.Env.Persistent.AgentGuid;
            var chat = context.Env.Persistent.Chat;
            var vectorStore = context.Env.Persistent.VectorStore;

            if (vectorStore is null)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ServiceError,
                    loc["node_err.memory_no_vector_store"],
                    nodeId: Id, nodeName: Name);
            }

            var (config, configError) = MemoryModelConfig.Load(kvData, EmbeddingProfileId);
            if (config is null)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    configError ?? loc["node_err.memory_no_model"],
                    nodeId: Id, nodeName: Name);
            }

            var recentMessages = context.Env.Transient.Fragments
                .Where(f => f.From != PromptBuilder.From.tool_result && !string.IsNullOrEmpty(f.Message.Content))
                .Reverse()
                .Take(Math.Max(1, RecentMessageCount))
                .Reverse()
                .ToList();

            if (recentMessages.Count == 0)
                return NodeResult.SuccessResult();

            var service = new MemoryIndexService(MemoryStoreProvider.Current, vectorStore, config);

            try
            {
                var hits = await service.SearchAsync(
                    MemoryPartition.ForAgent(agentGuid),
                    recentMessages.Select(x => x.Message.Content!).ToList(),
                    Math.Max(1, TopK),
                    SimilarityThreshold,
                    context.CancellationToken);

                MemoryRecallLog.Save(kvData, chat.Guid, new MemoryRecallSnapshot
                {
                    At = DateTimeOffset.UtcNow,
                    Status = "ok",
                    Model = config.ModelName,
                    Hits = hits.Take(10)
                        .Select(x => new MemoryRecallEntry(x.Id, x.Score, x.Content))
                        .ToList()
                });

                if (hits.Count == 0)
                    return NodeResult.SuccessResult();

                var memoryText = string.Join("\n---\n", hits.Select(x => x.Content).Distinct());
                var segment = new ContextSegment
                {
                    SourceType = typeof(MemoryRetrieveNode),
                    Message = new ChatMessage { Content = $"System Auto Recall Memories:\n{{\n{memoryText}\n}}" },
                    From = PromptBuilder.From.system,
                    Metadata = new Dictionary<string, object>
                    {
                        ["memoryCount"] = hits.Count,
                        ["memoryProfile"] = config.DisplayName
                    }
                };

                if (InsertPosition < 0 || InsertPosition >= context.Env.Transient.Fragments.Count)
                    context.Env.Transient.Fragments.Add(segment);
                else
                    context.Env.Transient.Fragments.Insert(InsertPosition, segment);

                return NodeResult.SuccessResult();
            }
            catch (MemoryNotReadyException ex)
            {
                MemoryRecallLog.Save(kvData, chat.Guid, new MemoryRecallSnapshot
                {
                    At = DateTimeOffset.UtcNow,
                    Status = ex.Status.ToString(),
                    Model = config.ModelName
                });

                return NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc.Format("node_err.memory_not_ready", ex.Status),
                    details: ex.Message,
                    nodeId: Id, nodeName: Name);
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ServiceError,
                    loc["node_err.memory_retrieve_failed"],
                    details: ex.ToString(),
                    nodeId: Id, nodeName: Name);
            }
        }
    }
}
