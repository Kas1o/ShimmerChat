using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class MemoryInject : IContextModifier
	{
		private static Dictionary<string, List<float>> VectorCachePool;
		IKVDataService KVData;
		public MemoryInject(IKVDataService _kv)
		{
			KVData = _kv;
			if(VectorCachePool == null)
			{
				VectorCachePool = new Dictionary<string, List<float>>();
			}
		}
		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "MemoryInject",
			Description = "Injects relevant memories into the prompt based on the latest (n) chat message. input value sep by, first parameter will be the message count(int), second parameter will be threshold(float32)."
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			// 0. 参数检查
			if (!int.TryParse(input.Split(',')[0], out int n) || n <= 0)
			{
				throw new InvalidDataException("Input must be a positive integer representing the number of recent messages to consider.");
			}
			if(n > 5)// 值太高时打印警告
			{
				Console.WriteLine("[MemoryInject] Warning: High message count may lead to performance issues.");
			}

			if(!float.TryParse(input.Split(',').ElementAtOrDefault(1) ?? "0.5", out float threshold) || threshold < 0.0f || threshold > 1.0f)
			{
				throw new InvalidDataException("Second parameter must be a float between 0.0 and 1.0 representing the similarity threshold.");
			}
			if( threshold < 0.2f)
			{
				Console.WriteLine("[MemoryInject] Warning: Low similarity threshold may lead to irrelevant memories being injected.");
			}



			// 1. 获取最新的n条非空消息。并按时间顺序分配权重。
			var x = chat.Messages
				.Where(x => !string.IsNullOrEmpty(x.message.Content))
				.Where(x => x.sender != Sender.ToolResult)
				.Reverse()
				.Take(n)
				.Reverse()
				.Index() // 越往后的值权重越大
				.Select(x => (x.Index+1, x.Item)) // 防止权重为0
				.ToList();

			// 2. 读配置创建 Client
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

			// 2.5 查看存在对应 collection 与否
			var collections = qclient.CollectionExistsAsync($"{agent.guid}").Result;
			if(!collections)
			{
				// 不存在则直接返回
				return;
			}

			// 3. 查下有没有缓存，没有就计算向量并缓存
			List<(List<float> vector, int weight)> vectors = new List<(List<float>, int)>();
			foreach(var item in x)
			{
				var msg = item.Item;
				if(!VectorCachePool.ContainsKey(msg.message.Content))
				{
					var vec = api.GetEmbedding(msg.message.Content).Result;
					VectorCachePool[msg.message.Content] = vec;
				}
				vectors.Add((VectorCachePool[msg.message.Content], item.Item1));
			}

			// 4. 查询Qdrant
			var searchResults = new List<(string Content, string UUID)>();
			foreach(var (vector, weight) in vectors)
			{
				var actual_threshold = (1f - (0.9f * (weight - 1) / (vectors.Aggregate((x,y) => x.weight > y.weight? x: y).weight - 1))) * threshold;
				var results = qclient.SearchAsync(
					collectionName: $"{agent.guid}",
					vector: vector.ToArray().AsMemory(),
					limit: 3,
					scoreThreshold: actual_threshold // 根据权重调整相似度阈值
				).Result;
				foreach (var res in results)
				{
					for(int i = 0; i < weight; i++) // 根据权重重复添加
					{
						searchResults.Add((res.Payload["content"].ToString(), res.Id.Uuid));
					}
				}
			}

			var memoryContext = string.Join("\n---\n", searchResults.Distinct());
			var newMessages = promptBuilder.Messages.ToList();
			newMessages.Add(($"System Auto Recall Memories:\n{{\n{memoryContext}\n}}", PromptBuilder.From.system));
			promptBuilder.Messages = newMessages.ToArray();
		}
	}
}
