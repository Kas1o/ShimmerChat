using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Memory
{
	public class MemoryTool : ITool
	{
		private IKVDataService KVData;
		public MemoryTool(IKVDataService kVData)
		{
			KVData = kVData;
		}

		async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
		{
			var action = JsonConvert.DeserializeObject<InputAction>(input);

			// 读配置创建 Client
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
			var vectorSize = ulong.Parse(KVData.Read("EmbeddingAPI", "vectorsize") ?? "1024");

			OpenAIAPI api = new OpenAIAPI(eUri, eApiKey, eModelName);

			// 查看存在对应 collection 与否
			var collections = qclient.CollectionExistsAsync($"{agent.guid}").Result;
			if (!collections)
			{
				// 创建 collection
				await qclient.CreateCollectionAsync(
					$"{agent.guid}",
					vectorsConfig: new VectorParams
					{
						Size = vectorSize,
						Distance = Distance.Cosine
					}
				);
			}


			switch (action.action)
			{
				case "add":
					var embd = await api.GetEmbedding(action.content);
					var point = new PointStruct
					{
						Id = Guid.NewGuid(),
						Vectors = embd.ToArray(),
						Payload =
						{
							["content"] = action.content,
							["timestamp"] = DateTime.UtcNow.ToString("o")
						}
					};
					await qclient.UpsertAsync(
						collectionName: $"{agent.guid}",
						points: [point]
					);

					return $"Memory added: {action.content}";
				case "delete":

					return $"还不会从向量数据库删东西，先不写了。";
				default:
					throw new InvalidOperationException("Invalid action specified.");
			}
		}

		Tool ITool.GetToolDefinition() => new Tool
		{
			name = "MemoryTool",
			description = "A tool for managing memory in conversations.",
			parameters = new List<(ToolParameter, bool)>
			{
				(new ToolParameter
				{
					name = "action",
					type = ParameterType.String,
					description = "The action to perform: add or delete.",
					@enum = ["add", "delete"]
				}, true),
				(new ToolParameter
				{
					name = "content",
					type = ParameterType.String,
					description = "The content to add or retrieve."
				}, false)
			}
		};

		struct InputAction
		{
			public string action { get; set; }
			public string content { get; set; }
		}
	}
}
