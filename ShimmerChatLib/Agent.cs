using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShimmerChatLib
{
    public class Agent
    {
		public Guid guid { get; set; }
		public string name { get; set; }
        public string description { get; set; } // The description of the agent

		public List<Chat> chats;

        private Agent()
        {

        }

        public void SaveTo(string path)
        {
			Directory.CreateDirectory(Path.Combine(path, "chats"));
            foreach (Chat chat in chats.Where(chat => chat.dirty))
            {
				chat.dirty = false;
                var saveData = JsonSerializer.Serialize(chat, new JsonSerializerOptions
				{
					WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
				});
                var savePath = Path.Combine(path, $"chats/{chat.Name}.json"); // Save each chat to a separate file
				File.WriteAllText(savePath, saveData);
			}
			File.WriteAllText(Path.Combine(path, "description.txt"), description);
			File.WriteAllText(Path.Combine(path, "name.txt"), name); // Save the agent's name to a text file
			File.WriteAllText(Path.Combine(path, "guid.txt"), guid.ToString()); // Save the agent's GUID to a text file
		}

        public static Agent ReadFrom(string path)
        {
			var chatFiles = new List<string>();
			// Load all chat files from the specified path
			string chatDir = Path.Combine(path, "chats");
			if (Directory.Exists(chatDir))
			{
				chatFiles.AddRange(Directory.GetFiles(chatDir, "*.json"));
			}
			// Create a new Agent instance
			Agent agent = new Agent
			{
				chats = new List<Chat>()
			};
			// Read each chat file and add it to the agent's chats
			foreach (var chatFile in chatFiles)
			{
				string chatJson = File.ReadAllText(chatFile);
				Chat chat = JsonSerializer.Deserialize<Chat>(chatJson);
				if (chat != null)
				{
					agent.AddChat(chat);
				}
			}
			// Read the description from the text file
			agent.description = File.ReadAllText(Path.Combine(path, "description.txt"));
			agent.name = File.ReadAllText(Path.Combine(path, "name.txt")); // Read the agent's name from the text file
			agent.guid = File.ReadAllText(Path.Combine(path, "guid.txt")) is string guidString && Guid.TryParse(guidString, out Guid parsedGuid)
				? parsedGuid
				: Guid.NewGuid(); // Read the agent's GUID from the text file or generate a new one if it fails to parse
			return agent;
		}
        public void AddChat(Chat chat)
        {
			// 判断 是否已经存在同名的聊天
			if (chats.Any(c => c.Name == chat.Name))
			{
				throw new InvalidOperationException($"A chat with the name '{chat.Name}' already exists.");
			}
			chats.Add(chat);
		}

		public static Agent Create(string Name, string desc)
		{
			return new Agent
			{
				chats = new List<Chat>(),
				name = Name,
				description = desc,
				guid= Guid.NewGuid(),
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
