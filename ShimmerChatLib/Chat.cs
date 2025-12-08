using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib
{
    public class Chat
    {
		public required string Name {
			get => field;
			set
			{
				field = value;
			}
		} // The name of the chat
		public Guid Guid { get; set; } // The unique identifier of the chat
		public ObservableCollection<Message> Messages { get;set; } // List of messages in the chat

		public Chat()
		{
			Messages = new ObservableCollection<Message>();
			Guid = Guid.NewGuid();
		}

		public void Save(IKVDataService kvDataService)
		{
            var chatJson = JsonConvert.SerializeObject(this);
            kvDataService.Write("Chats", Guid.ToString(), chatJson);
        }

        public static Chat Load(Guid guid, IKVDataService kvDataService)
        {
            var chatJson = kvDataService.Read("Chats", guid.ToString());
            if (chatJson == null)
            {
                throw new InvalidOperationException($"Chat with GUID '{guid}' not found.");
            }
            var chat = JsonConvert.DeserializeObject<Chat>(chatJson);
            return chat;
        }

		public void AddMessage(Message message, IKVDataService kvDataService = null)
		{
			Messages.Add(message);
            // Save the chat if kvDataService is provided
            kvDataService?.Write("Chats", Guid.ToString(), JsonConvert.SerializeObject(this));
		}
	}
}
