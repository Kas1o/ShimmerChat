using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class FileMessageStoreService : IMessageStoreService
    {
        private readonly string _root;

        public class OrderEntry
        {
            public Guid MessageGuid { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public string RootPath => _root;

        public FileMessageStoreService()
        {
            _root = Path.Combine(AppContext.BaseDirectory, "ChatMessages");
            if (!Directory.Exists(_root))
                Directory.CreateDirectory(_root);
        }

        public void InsertMessage(Guid chatGuid, Message message)
        {
            if (message.Id == Guid.Empty)
                message.Id = Guid.NewGuid();

            var chatDir = GetChatDir(chatGuid);
            var order = LoadOrder(chatDir);

            order.Add(new OrderEntry { MessageGuid = message.Id, Timestamp = message.timestamp });
            SaveOrder(chatDir, order);

            SaveMessageFile(chatDir, message);
        }

        public void UpdateMessage(Guid chatGuid, Message message)
        {
            var chatDir = GetChatDir(chatGuid);
            var msgPath = GetMessagePath(chatDir, message.Id);

            if (File.Exists(msgPath))
            {
                SaveMessageFile(chatDir, message);
            }
            else
            {
                InsertMessage(chatGuid, message);
            }
        }

        public void DeleteMessage(Guid chatGuid, Message message)
        {
            var chatDir = GetChatDir(chatGuid);
            var order = LoadOrder(chatDir);
            order.RemoveAll(x => x.MessageGuid == message.Id);
            SaveOrder(chatDir, order);

            var msgPath = GetMessagePath(chatDir, message.Id);
            if (File.Exists(msgPath))
                File.Delete(msgPath);
        }

        public Message? GetMessage(Guid chatGuid, Guid messageGuid)
        {
            var chatDir = GetChatDir(chatGuid);
            var msgPath = GetMessagePath(chatDir, messageGuid);
            if (!File.Exists(msgPath))
                return null;

            var json = File.ReadAllText(msgPath);
            return DeserializeMessage(json);
        }

        public List<Message> GetMessages(Guid chatGuid, int skip, int take)
        {
            var chatDir = GetChatDir(chatGuid);
            var order = LoadOrder(chatDir);
            var sorted = order.OrderBy(x => x.Timestamp).Skip(skip).Take(take).ToList();

            var messages = new List<Message>();
            foreach (var entry in sorted)
            {
                var msgPath = GetMessagePath(chatDir, entry.MessageGuid);
                if (File.Exists(msgPath))
                {
                    var json = File.ReadAllText(msgPath);
                    var msg = DeserializeMessage(json);
                    if (msg != null)
                        messages.Add(msg);
                }
            }
            return messages;
        }

        public int GetMessageCount(Guid chatGuid)
        {
            var chatDir = GetChatDir(chatGuid);
            if (!Directory.Exists(chatDir))
                return 0;

            var order = LoadOrder(chatDir);
            return order.Count;
        }

        public void DeleteAllMessages(Guid chatGuid)
        {
            var chatDir = GetChatDir(chatGuid);
            if (Directory.Exists(chatDir))
            {
                Directory.Delete(chatDir, true);
            }
        }

        public string? GetLastMessagePreview(Guid chatGuid)
        {
            var chatDir = GetChatDir(chatGuid);
            var order = LoadOrder(chatDir);
            var lastEntry = order.OrderByDescending(x => x.Timestamp).FirstOrDefault();
            if (lastEntry == null)
                return string.Empty;

            var msgPath = GetMessagePath(chatDir, lastEntry.MessageGuid);
            if (!File.Exists(msgPath))
                return string.Empty;

            var json = File.ReadAllText(msgPath);
            var msg = DeserializeMessage(json);
            var content = msg?.message?.Content;
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            return content.Length > 50 ? content[..50] + "..." : content;
        }

        public IEnumerable<Guid> GetAllChatGuids()
        {
            if (!Directory.Exists(_root))
                yield break;

            foreach (var dir in Directory.GetDirectories(_root))
            {
                if (Guid.TryParse(Path.GetFileName(dir), out var guid))
                    yield return guid;
            }
        }

        private string GetChatDir(Guid chatGuid)
        {
            var dir = Path.Combine(_root, chatGuid.ToString());
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetMessagePath(string chatDir, Guid messageGuid)
        {
            return Path.Combine(chatDir, $"{messageGuid}.json");
        }

        private static List<OrderEntry> LoadOrder(string chatDir)
        {
            var orderPath = Path.Combine(chatDir, "_order.json");
            if (!File.Exists(orderPath))
                return new List<OrderEntry>();

            var json = File.ReadAllText(orderPath);
            return JsonConvert.DeserializeObject<List<OrderEntry>>(json) ?? new List<OrderEntry>();
        }

        private static void SaveOrder(string chatDir, List<OrderEntry> order)
        {
            var orderPath = Path.Combine(chatDir, "_order.json");
            var json = JsonConvert.SerializeObject(order, Formatting.Indented);
            File.WriteAllText(orderPath, json);
        }

        private static void SaveMessageFile(string chatDir, Message message)
        {
            var msgPath = Path.Combine(chatDir, $"{message.Id}.json");
            var json = JsonConvert.SerializeObject(message, Formatting.Indented);
            File.WriteAllText(msgPath, json);
        }

        private static Message DeserializeMessage(string json)
        {
            var msg = JsonConvert.DeserializeObject<Message>(json)
                ?? throw new InvalidOperationException("Failed to deserialize message");
            return msg;
        }
    }
}
