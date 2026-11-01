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

            /// <summary>会话内顺序号。&lt; 0 表示 Order 字段出现前写入的老数据。</summary>
            public long Order { get; set; } = -1;

            /// <summary>仅用于老数据（Order 缺失）的回退排序。</summary>
            public DateTime Timestamp { get; set; }
            public string Sender { get; set; } = string.Empty;
            public string MessageJson { get; set; } = string.Empty;
        }

        public LiteDBMessageStoreService(LiteDatabase database)
        {
            _database = database;
            _collection = _database.GetCollection<MessageDocument>("messages");
            _collection.EnsureIndex(x => x.ChatGuid);
            _collection.EnsureIndex(x => x.Order);
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
                Order = message.Order,
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
                existing.Order = message.Order;
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
            var query = _collection.Find(x => x.ChatGuid == chatGuid);

            // 存在未分配顺序号的消息 → 整组回退到 timestamp（等于 Order 出现前的行为）；
            // 否则按 Order 排序。混合状态统一走回退，避免"一半有序一半乱"。
            var sorted = HasUnassignedOrder(chatGuid)
                ? query.OrderBy(x => x.Timestamp)
                : query.OrderBy(x => x.Order);

            var docs = sorted.Skip(skip).Take(take).ToList();
            return docs.Select(d => DeserializeMessage(d.MessageJson)).ToList();
        }

        private bool HasUnassignedOrder(Guid chatGuid)
            => _collection.Exists(x => x.ChatGuid == chatGuid && x.Order < 0);

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
            var query = _collection.Find(x => x.ChatGuid == chatGuid);
            var doc = HasUnassignedOrder(chatGuid)
                ? query.OrderByDescending(x => x.Timestamp).FirstOrDefault()
                : query.OrderByDescending(x => x.Order).FirstOrDefault();

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
