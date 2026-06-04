using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
		}
		public Guid Guid { get; set; }

		[JsonIgnore]
		public ObservableCollection<Message> Messages { get; set; }

		public DateTime CreateTime { get; set; }
		public DateTime LastModifyTime { get; set; }
		public string LastMessagePreview { get; set; } = string.Empty;
		public int MessageCount { get; set; }

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
            chat.Messages = new ObservableCollection<Message>();
            return chat;
        }

        public static Chat LoadAndMigrate(Guid guid, IKVDataService kvDataService, IMessageStoreService messageStore)
        {
            var chatJson = kvDataService.Read("Chats", guid.ToString());
            if (chatJson == null)
            {
                throw new InvalidOperationException($"Chat with GUID '{guid}' not found.");
            }

            var chat = JsonConvert.DeserializeObject<Chat>(chatJson);
            chat.Messages = new ObservableCollection<Message>();

            int existingCount = messageStore.GetMessageCount(guid);
            if (existingCount == 0)
            {
                try
                {
                    var legacy = JsonConvert.DeserializeObject<LegacyChatData>(chatJson);
                    if (legacy?.Messages != null && legacy.Messages.Count > 0)
                    {
                        foreach (var msg in legacy.Messages)
                        {
                            if (msg.Id == Guid.Empty) msg.Id = Guid.NewGuid();
                            messageStore.InsertMessage(guid, msg);
                        }
                        chat.LastMessagePreview = messageStore.GetLastMessagePreview(guid) ?? string.Empty;
                        chat.MessageCount = legacy.Messages.Count;
                        chat.Save(kvDataService);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Migration warning for chat {guid}: {ex.Message}");
                }
            }
            else
            {
                chat.MessageCount = existingCount;
                chat.LastMessagePreview = messageStore.GetLastMessagePreview(guid) ?? string.Empty;
            }

            return chat;
        }

        private class LegacyChatData
        {
            public List<Message>? Messages { get; set; }
        }

		public void AddMessage(Message message)
		{
			Messages.Add(message);
		}

		public void LoadMessages(IMessageStoreService messageStore, int skip, int take)
		{
			if (Messages == null)
				Messages = new ObservableCollection<Message>();
			else
				Messages.Clear();

			var loaded = messageStore.GetMessages(Guid, skip, take);
			foreach (var msg in loaded)
				Messages.Add(msg);

			MessageCount = messageStore.GetMessageCount(Guid);
			LastMessagePreview = messageStore.GetLastMessagePreview(Guid) ?? string.Empty;
		}

		public void SaveMessage(IMessageStoreService messageStore, Message message)
		{
			var existing = messageStore.GetMessage(Guid, message.Id);
			if (existing != null)
				messageStore.UpdateMessage(Guid, message);
			else
				messageStore.InsertMessage(Guid, message);

			MessageCount = messageStore.GetMessageCount(Guid);
			LastMessagePreview = messageStore.GetLastMessagePreview(Guid) ?? string.Empty;
		}

		public void DeleteMessage(IMessageStoreService messageStore, Message message)
		{
			messageStore.DeleteMessage(Guid, message);
			Messages.Remove(message);

			MessageCount = messageStore.GetMessageCount(Guid);
			LastMessagePreview = messageStore.GetLastMessagePreview(Guid) ?? string.Empty;
		}

		public void SaveAllMessages(IMessageStoreService messageStore)
		{
			messageStore.DeleteAllMessages(Guid);
			foreach (var message in Messages)
			{
				messageStore.InsertMessage(Guid, message);
			}
			MessageCount = Messages.Count;
			LastMessagePreview = messageStore.GetLastMessagePreview(Guid) ?? string.Empty;
		}
	}
}
