using System;
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

		public List<Guid> chatGuids
        {
            get => field;
            set
            {
                field = value;
            }
        }

        private Agent()
        {

        }

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

		public static Agent Create(string Name, string desc, string greeting = null)
		{
			return new Agent
			{
				chatGuids = new List<Guid>(),
				name = Name,
				description = desc,
				guid= Guid.NewGuid(),
				greeting = greeting
			};
		}

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
	}
}
