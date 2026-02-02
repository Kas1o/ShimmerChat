﻿﻿﻿﻿﻿﻿using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib
{
    public class Agent
    {
		public Guid guid { get; set; }
		public string name { get; set; }
        public string description { get; set; } // The description of the agent
		public string greeting { get; set; }
		public List<string> alternativeGreetings { get; set; } = new List<string>(); // Alternative greetings for the agent
		public List<Guid> chatGuids
        {
            get => field;
            set
            {
                field = value;
            }
        }
        public Guid? AvatarGuid { get; set; }
        public Guid? BackgroundGuid { get; set; }
        public List<string> CustomToolNames { get; set; } = new List<string>(); // Custom tools for this agent

		#region Export & Import
        public string Export(bool clearChat = true)
        {
            var copy = this.MemberwiseClone() as Agent;
            if (clearChat)
                copy.chatGuids = [];

            string? avatar = null;
            string? background = null;
			string AvatarPath = $"{AppContext.BaseDirectory}/UserUploadImage/{this.AvatarGuid}.png";
			string BackgroudPath = $"{AppContext.BaseDirectory}/UserUploadImage/{this.BackgroundGuid}.png";

			if (AvatarGuid != null)
            if (File.Exists(AvatarPath))
            {
                var content = File.ReadAllBytes(AvatarPath);
                avatar = Convert.ToBase64String(content);
            }

			if (BackgroundGuid != null)
			if (File.Exists(BackgroudPath))
			{
				var content = File.ReadAllBytes(BackgroudPath);
				background = Convert.ToBase64String(content);
			}


            return JsonConvert.SerializeObject(new AgentExportStructure
            (
                Agent : copy!,
                AvatarBase64 : avatar!,
                BackgroundBase64 : background!
            ), new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
            });
        }

        public static Agent Import(string importJson, bool clearChat = true)
        {
            var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(importJson);
            var agent = importStructure.Agent;
            if (clearChat)
            {
                agent.chatGuids = new List<Guid>();
            }
            if (!string.IsNullOrEmpty(importStructure.AvatarBase64))
            {
                var avatarBytes = Convert.FromBase64String(importStructure.AvatarBase64);
                var avatarGuid = Guid.NewGuid();
                var avatarPath = $"{AppContext.BaseDirectory}/UserUploadImage/{avatarGuid}.png";
                File.WriteAllBytes(avatarPath, avatarBytes);
                agent.AvatarGuid = avatarGuid;
            }
            if (!string.IsNullOrEmpty(importStructure.BackgroundBase64))
            {
                var backgroundBytes = Convert.FromBase64String(importStructure.BackgroundBase64);
                var backgroundGuid = Guid.NewGuid();
                var backgroundPath = $"{AppContext.BaseDirectory}/UserUploadImage/{backgroundGuid}.png";
                File.WriteAllBytes(backgroundPath, backgroundBytes);
                agent.BackgroundGuid = backgroundGuid;
            }
            return agent;
		}

		#endregion
		#region Save & Load
		public void Save(IKVDataService kvDataService)
        {
            var agentJson = JsonConvert.SerializeObject(this);
            kvDataService.Write("Agents", guid.ToString(), agentJson);
        }

        public static Agent Load(Guid guid, IKVDataService kvDataService)
        {
            var agentJson = kvDataService.Read("Agents", guid.ToString());
            if (agentJson == null)
            {
                throw new InvalidOperationException($"Agent with GUID '{guid}' not found.");
            }
            return JsonConvert.DeserializeObject<Agent>(agentJson);
        }
		#endregion
		#region Statics
		public static List<Guid> GetAllAgentGuids(IKVDataService kvDataService)
        {
            var agentsJson = kvDataService.Read("Agents", "__AllAgents__");
            if (agentsJson == null)
            {
                return new List<Guid>();
            }
            return JsonConvert.DeserializeObject<List<Guid>>(agentsJson);
        }

        public static void SaveAllAgentGuids(IKVDataService kvDataService, List<Guid> agentGuids)
        {
            var agentsJson = JsonConvert.SerializeObject(agentGuids);
            kvDataService.Write("Agents", "__AllAgents__", agentsJson);
        }

        public static void AddAgentToAll(Agent agent, IKVDataService kvDataService)
        {
            var agentGuids = GetAllAgentGuids(kvDataService);
            if (!agentGuids.Contains(agent.guid))
            {
                agentGuids.Add(agent.guid);
                SaveAllAgentGuids(kvDataService, agentGuids);
            }
        }

        public static void RemoveAgentFromAll(Agent agent, IKVDataService kvDataService)
        {
            var agentGuids = GetAllAgentGuids(kvDataService);
            if (agentGuids.Contains(agent.guid))
            {
                agentGuids.Remove(agent.guid);
                SaveAllAgentGuids(kvDataService, agentGuids);
            }
        }
		#endregion
		#region ChatUtil
		public void AddChatGuid(Guid chatGuid)
        {
            if (!chatGuids.Contains(chatGuid))
            {
                chatGuids.Add(chatGuid);
            }
        }

        public void RemoveChatGuid(Guid chatGuid)
        {
            chatGuids.Remove(chatGuid);
        }

        public List<Chat> GetChats(IKVDataService kvDataService)
        {
            var chats = new List<Chat>();
            foreach (var chatGuid in chatGuids)
            {
                try
                {
                    var chat = Chat.Load(chatGuid, kvDataService);
                    chats.Add(chat);
                }
                catch (Exception ex)
                {
                    // Handle exception if chat fails to load
                    Console.WriteLine($"Failed to load chat with GUID '{chatGuid}': {ex.Message}");
                }
            }
            chats = chats.OrderBy(x => x.LastModifyTime).Reverse().ToList();
            return chats;
        }

        /// <summary>
        /// 获取指定范围的聊天列表，按 LastModifyTime 降序排序
        /// 只加载指定范围内的聊天，用于虚拟化列表的惰性加载
        /// </summary>
        public List<Chat> GetChatsRange(IKVDataService kvDataService, int startIndex, int count)
        {
            // 首先获取所有 chatGuid 和它们的 LastModifyTime（轻量级元数据）
            // 使用 DateTime.MinValue 作为默认值，确保即使读取失败也能包含该聊天
            var chatMetadata = new List<(Guid Guid, DateTime LastModifyTime)>();
            foreach (var chatGuid in chatGuids)
            {
                try
                {
                    // 尝试从 KV 存储中只读取元数据（LastModifyTime）
                    var lastModifyTime = GetChatLastModifyTime(chatGuid, kvDataService);
                    // 如果读取失败，使用 DateTime.MinValue 作为默认值，确保聊天仍然被包含
                    chatMetadata.Add((chatGuid, lastModifyTime ?? DateTime.MinValue));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get metadata for chat '{chatGuid}': {ex.Message}");
                    // 即使失败也添加该聊天，使用最小时间值
                    chatMetadata.Add((chatGuid, DateTime.MinValue));
                }
            }

            // 按 LastModifyTime 降序排序
            var sortedGuids = chatMetadata
                .OrderByDescending(x => x.LastModifyTime)
                .Select(x => x.Guid)
                .Skip(startIndex)
                .Take(count)
                .ToList();

            // 只加载指定范围内的完整聊天数据
            var chats = new List<Chat>();
            foreach (var chatGuid in sortedGuids)
            {
                try
                {
                    var chat = Chat.Load(chatGuid, kvDataService);
                    chats.Add(chat);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load chat with GUID '{chatGuid}': {ex.Message}");
                }
            }

            return chats;
        }

        /// <summary>
        /// 获取聊天总数
        /// </summary>
        public int GetChatCount()
        {
            return chatGuids.Count;
        }

        /// <summary>
        /// 从 KV 存储中只读取聊天的 LastModifyTime
        /// </summary>
        private DateTime? GetChatLastModifyTime(Guid chatGuid, IKVDataService kvDataService)
        {
            try
            {
                var chatJson = kvDataService.Read("Chats", chatGuid.ToString());
                if (string.IsNullOrEmpty(chatJson))
                    return null;

                // 使用 Json.NET 只解析需要的字段
                using var reader = new JsonTextReader(new StringReader(chatJson));
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.PropertyName && reader.Value?.ToString() == "LastModifyTime")
                    {
                        reader.Read();
                        if (reader.TokenType == JsonToken.Date)
                        {
                            return (DateTime)reader.Value;
                        }
                        else if (reader.TokenType == JsonToken.String)
                        {
                            if (DateTime.TryParse(reader.Value?.ToString(), out var dateTime))
                            {
                                return dateTime;
                            }
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public Chat GetChat(Guid chatGuid, IKVDataService kvDataService)
        {
            if (!chatGuids.Contains(chatGuid))
            {
                throw new InvalidOperationException($"Chat with GUID '{chatGuid}' not found in agent.");
            }
            return Chat.Load(chatGuid, kvDataService);
        }
		#endregion
        /// <summary>
        /// 留由 Newtonsoft.Json 使用。
        /// </summary>
        private Agent()
        {

        }

		public static Agent Create(string Name, string desc, string greeting = null, List<string> alternativeGreetings = null)
		{
			return new Agent
			{
				chatGuids = new List<Guid>(),
				name = Name,
				description = desc,
				guid= Guid.NewGuid(),
				greeting = greeting,
				alternativeGreetings = alternativeGreetings ?? new List<string>(),
				CustomToolNames = new List<string>()
			};
		}
		#region Equal
		public override bool Equals(object? obj)
		{
			if(obj is Agent agent)
			{
				return agent.guid == this.guid;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return guid.GetHashCode();
		}
		#endregion
	}

    /// <summary>
    /// 导出的 Agent 结构。
    /// 因为 Agent 保存的是图像的 Guid，所以导出时需要包含图像的 Base64 编码。
    /// </summary>
    public record AgentExportStructure
    (
        Agent? Agent,
        string? AvatarBase64,
        string? BackgroundBase64
    );
}
