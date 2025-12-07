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
using static Qdrant.Client.Grpc.Conditions;


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
			var vectorSize = ulong.Parse(KVData.Read("QdrantAPI", "dim") ?? "1024");

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
					if(action.content == null)
						return "Content cannot be null when adding memory.";
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

					return $"Memory added: {action.content} {point.Id}";
				case "delete":
					if (action.id == null)
						return "ID cannot be null when deleting memory.";
					var result = await qclient.DeleteAsync(
						collectionName: $"{agent.guid}",
						id: Guid.Parse(action.id)
					);
					return $"Memory with ID {action.id} deleted with result status {result.Status}.";
				case "search":
					if (action.content == null)
						return "Content cannot be null when searching memory.";
					if (action.search_mode == "similarity")
					{
						var searchEmbd = await api.GetEmbedding(action.content);
						var searchResult = await qclient.SearchAsync(
							collectionName: $"{agent.guid}",
							vector: searchEmbd.ToArray(),
							limit: 5
						 );
						StringBuilder sb = new StringBuilder();
					 sb.AppendLine("Search Results:");
						foreach (var res in searchResult)
						{
							if (res.Payload != null && res.Payload.ContainsKey("content"))
							{
								sb.AppendLine($"ID: {res.Id}, Score: {res.Score}, Content: {res.Payload["content"]}, Timestamp: { res.Payload["timestamp"]}");
							}
						}
						return sb.ToString();
					}
					else if(action.search_mode == "keyword")
					{
						var filter = MatchText("content", action.content);
						var searchResult = await qclient.ScrollAsync(
							collectionName: $"{agent.guid}",
							filter: filter,
							limit: 5
						 );
						StringBuilder sb = new StringBuilder();
						sb.AppendLine("Search Results:");
						foreach (var res in searchResult.Result)
						{
							if (res.Payload != null && res.Payload.ContainsKey("content"))
							{
								sb.AppendLine($"ID: {res.Id}, Content: {res.Payload["content"]}");
							}
						}
						return sb.ToString();
					}


					return "Unknown search mode.";

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
					@enum = ["add", "delete", "search"]
				}, true),
				(new ToolParameter
				{
					name = "content",
					type = ParameterType.String,
					description = "The content to add or retrieve. (example value: \"Users prefer beef over other foods.\")"
				}, false),
				(new ToolParameter
				{
					name = "id",
					type = ParameterType.String,
					description = "The ID of the memory to delete. (example value: \"7ac1ceca-4fc7-43c5-8ff3-02376c9c9a97\")"
				}, false),
				(new ToolParameter
				{
					name = "search_mode",
					type = ParameterType.String,
					description = "The search mode to use.",
					@enum = ["similarity", "keyword"]
				}, false)
			}
		};

		struct InputAction
		{
			public string action { get; set; }
			public string? content { get; set; }
			public string? id { get; set; }
			public string search_mode { get; set; }
		}
	}
}
