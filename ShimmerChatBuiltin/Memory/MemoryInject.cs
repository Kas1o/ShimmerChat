using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Memory
{
	public class MemoryInjectConfig : ModifierConfig
	{
		[UiHint("参考消息数", "用于检索记忆的最近消息数量", Min = 1, Max = 5)]
		public int RecentMessageCount { get; set; } = 3;

		[UiHint("相似度阈值", "最低相似度 (0.0 ~ 1.0)", Min = 0.0, Max = 1.0)]
		public float SimilarityThreshold { get; set; } = 0.5f;
	}

	public class MemoryInject : IContextModifier
	{
		private static Dictionary<string, List<float>>? VectorCachePool;
		IKVDataService KVData;

		public MemoryInject(IKVDataService _kv)
		{
			KVData = _kv;
			if (VectorCachePool == null)
			{
				VectorCachePool = new Dictionary<string, List<float>>();
			}
		}

		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "MemoryInject",
			Description = "Injects relevant memories into the prompt based on the latest (n) chat message."
		};

		public Type ConfigType => typeof(MemoryInjectConfig);

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			var cfg = (MemoryInjectConfig)config;
			if (cfg.RecentMessageCount <= 0)
				return (false, "RecentMessageCount must be greater than 0");
			if (cfg.SimilarityThreshold < 0f || cfg.SimilarityThreshold > 1f)
				return (false, "SimilarityThreshold must be between 0.0 and 1.0");
			return (true, "");
		}

		void IContextModifier.ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (MemoryInjectConfig)config;
			var n = cfg.RecentMessageCount;
			var threshold = cfg.SimilarityThreshold;

			if (n > 5)
			{
				Console.WriteLine("[MemoryInject] Warning: High message count may lead to performance issues.");
			}
			if (threshold < 0.2f)
			{
				Console.WriteLine("[MemoryInject] Warning: Low similarity threshold may lead to irrelevant memories being injected.");
			}

			var x = chat.Messages
				.Where(x => !string.IsNullOrEmpty(x.message.Content))
				.Where(x => x.sender != Sender.ToolResult)
				.Reverse()
				.Take(n)
				.Reverse()
				.Index()
				.Select(x => (x.Index + 1, x.Item))
				.ToList();

			var QHost = KVData.Read("QdrantAPI", "host") ?? throw new InvalidDataException("Qdrant API host not set.");
			var Qport = KVData.Read("QdrantAPI", "port") ?? throw new InvalidDataException("Qdrant API port not set.");
			var QApiKey = KVData.Read("QdrantAPI", "apikey");

			var qclient = new QdrantClient(QHost, int.Parse(Qport));
			if (!string.IsNullOrEmpty(QApiKey))
			{
				var chnl = QdrantChannel.ForAddress(new Uri($"http://{QHost}:{Qport}"), new ClientConfiguration { ApiKey = QApiKey });
				qclient = new QdrantClient(new QdrantGrpcClient(chnl));
			}

			var eUri = KVData.Read("EmbeddingAPI", "uri") ?? string.Empty;
			var eApiKey = KVData.Read("EmbeddingAPI", "apikey") ?? string.Empty;
			var eModelName = KVData.Read("EmbeddingAPI", "modelname") ?? string.Empty;

			OpenAIAPI api = new OpenAIAPI(eUri, eApiKey, eModelName);

			var collections = qclient.CollectionExistsAsync($"{agent.Guid}").Result;
			if (!collections)
			{
				return;
			}

			List<(List<float> vector, int weight)> vectors = new List<(List<float>, int)>();
			foreach (var item in x)
			{
				var msg = item.Item;
				if (!VectorCachePool!.ContainsKey(msg.message.Content))
				{
					var vec = api.GetEmbedding(msg.message.Content).Result;
					VectorCachePool[msg.message.Content] = vec;
				}
				vectors.Add((VectorCachePool[msg.message.Content], item.Item1));
			}

			var searchResults = new List<(string Content, string UUID)>();
			foreach (var (vector, weight) in vectors)
			{
				var actual_threshold = (1f - (0.9f * (weight - 1) / (vectors.Aggregate((x, y) => x.weight > y.weight ? x : y).weight - 1))) * threshold;
				var results = qclient.SearchAsync(
					collectionName: $"{agent.Guid}",
					vector: vector.ToArray().AsMemory(),
					limit: 3,
					scoreThreshold: actual_threshold
				).Result;
				foreach (var res in results)
				{
					for (int i = 0; i < weight; i++)
					{
						searchResults.Add((res.Payload["content"].ToString(), res.Id.Uuid));
					}
				}
			}

			var memoryContext = string.Join("\n---\n", searchResults.Distinct().Select(r => r.Content));
			context.Segments.Add(new ContextSegment
			{
				SourceType = typeof(MemoryInject),
				Message = new ChatMessage { Content = $"System Auto Recall Memories:\n{{\n{memoryContext}\n}}" },
				From = PromptBuilder.From.system,
				Metadata = new Dictionary<string, object>
				{
					["memoryCount"] = searchResults.Count
				}
			});
		}
	}
}
