using LiteDB;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class LiteDBMessageStoreService : IMessageStoreService
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<MessageDocument> _collection;

        public class MessageDocument
        {
            public ObjectId Id { get; set; } = ObjectId.NewObjectId();
            public Guid MessageGuid { get; set; }
            public Guid ChatGuid { get; set; }
            public DateTime Timestamp { get; set; }
            public string Sender { get; set; } = string.Empty;
            public string MessageJson { get; set; } = string.Empty;
        }

        public LiteDBMessageStoreService(LiteDatabase database)
        {
            _database = database;
            _collection = _database.GetCollection<MessageDocument>("messages");
            _collection.EnsureIndex(x => x.ChatGuid);
            _collection.EnsureIndex(x => x.Timestamp);
            _collection.EnsureIndex(x => x.MessageGuid, true);
        }

        public void InsertMessage(Guid chatGuid, Message message)
        {
            if (message.Id == Guid.Empty)
                message.Id = Guid.NewGuid();

            var doc = new MessageDocument
            {
                MessageGuid = message.Id,
                ChatGuid = chatGuid,
                Timestamp = message.timestamp,
                Sender = message.sender,
                MessageJson = JsonConvert.SerializeObject(message)
            };
            _collection.Insert(doc);
        }

        public void UpdateMessage(Guid chatGuid, Message message)
        {
            var existing = _collection.FindOne(x => x.MessageGuid == message.Id);
            if (existing != null)
            {
                existing.Timestamp = message.timestamp;
                existing.Sender = message.sender;
                existing.MessageJson = JsonConvert.SerializeObject(message);
                _collection.Update(existing);
            }
            else
            {
                InsertMessage(chatGuid, message);
            }
        }

        public void DeleteMessage(Guid chatGuid, Message message)
        {
            _collection.DeleteMany(x => x.MessageGuid == message.Id);
        }

        public Message? GetMessage(Guid chatGuid, Guid messageGuid)
        {
            var doc = _collection.FindOne(x => x.MessageGuid == messageGuid);
            return doc != null ? DeserializeMessage(doc.MessageJson) : null;
        }

        public List<Message> GetMessages(Guid chatGuid, int skip, int take)
        {
            var docs = _collection
                .Find(x => x.ChatGuid == chatGuid)
                .OrderBy(x => x.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToList();

            return docs.Select(d => DeserializeMessage(d.MessageJson)).ToList();
        }

        public int GetMessageCount(Guid chatGuid)
        {
            return _collection.Count(x => x.ChatGuid == chatGuid);
        }

        public void DeleteAllMessages(Guid chatGuid)
        {
            _collection.DeleteMany(x => x.ChatGuid == chatGuid);
        }

        public string? GetLastMessagePreview(Guid chatGuid)
        {
            var doc = _collection
                .Find(x => x.ChatGuid == chatGuid)
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefault();

            if (doc == null)
                return string.Empty;

            var msg = DeserializeMessage(doc.MessageJson);
            var content = msg?.message?.Content;
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            return content.Length > 50 ? content[..50] + "..." : content;
        }

        public IEnumerable<Guid> GetAllChatGuids()
        {
            return _collection.FindAll()
                .Select(x => x.ChatGuid)
                .Distinct()
                .ToList();
        }

        private static Message DeserializeMessage(string json)
        {
            var msg = JsonConvert.DeserializeObject<Message>(json)
                ?? throw new InvalidOperationException("Failed to deserialize message");
            return msg;
        }
    }
}
