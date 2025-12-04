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
			Description = "Injects relevant memories into the prompt based on the latest (n) chat message. (input value : int) will be the message count"
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			// 1. 获取最新的n条非空消息。并按时间顺序分配权重。
			var x = chat.Messages
				.Where(x => !string.IsNullOrEmpty(x.message.Content))
				.Reverse()
				.Take(int.Parse(input))
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
			var searchResults = new List<string>();
			foreach(var (vector, weight) in vectors)
			{
				var results = qclient.SearchAsync(
					collectionName: $"{agent.guid}",
					vector: vector.ToArray().AsMemory(),
					limit: 3,
					scoreThreshold: MathF.Sin(((float)weight / float.Parse(input)) * MathF.PI / 2) * 0.8f // 根据权重调整相似度阈值
				).Result;//System.AggregateException:“One or more errors occurred. (Status(StatusCode="InvalidArgument", Detail="Wrong input: Not existing vector name error: "))”
				foreach (var res in results)
				{
					for(int i = 0; i < weight; i++) // 根据权重重复添加
					{
						searchResults.Add(res.Payload["content"].ToString());
					}
				}
			}

			var memoryContext = string.Join("\n---\n", searchResults.Distinct());
			var newMessages = promptBuilder.Messages.ToList();
			newMessages.Add(($"Memories:\n{{\n{memoryContext}\n}}", PromptBuilder.From.system));
			promptBuilder.Messages = newMessages.ToArray();
		}
	}
}
