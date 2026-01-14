using System;
using System.Buffers.Text;
using System.Collections.Generic;
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
				alternativeGreetings = alternativeGreetings ?? new List<string>()
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

    public record AgentExportStructure
    (
        Agent? Agent,
        string? AvatarBase64,
        string? BackgroundBase64
    );
}
