using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib
{
    public class Chat
    {
		private static ILogger<Chat>? _logger;

		static Chat()
		{
		}

		internal static void SetLogger(ILogger<Chat> logger)
		{
			_logger = logger;
		}

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

		/// <summary>
		/// 来源提供器类型名。null = 用户直接对话；
		/// 非 null = 由生成提供器（CRON、Hook 等）创建的会话。
		/// </summary>
		public string? ProviderSource { get; set; }

		/// <summary>
		/// 来源生成事件 Id（与 <see cref="ProviderSource"/> 配套）
		/// </summary>
		public string? SourceEventId { get; set; }

		/// <summary>
		/// 触发时由提供器盖印的渲染修改树覆盖 JSON（null = 继承 Agent）。
		/// 随 Chat 持久化，保证后续渲染始终使用生成时的树，
		/// 不受事件配置后续修改影响。
		/// </summary>
		public string? SourceRenderModifierTreeJson { get; set; }

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
                        for (int i = 0; i < legacy.Messages.Count; i++)
                        {
                            var msg = legacy.Messages[i];
                            if (msg.Id == Guid.Empty) msg.Id = Guid.NewGuid();
                            // 旧格式没有顺序号，按原数组顺序直接赋值（InsertMessage 会原样保存）
                            msg.Order = i;
                            messageStore.InsertMessage(guid, msg);
                        }
                        chat.LastMessagePreview = messageStore.GetLastMessagePreview(guid) ?? string.Empty;
                        chat.MessageCount = legacy.Messages.Count;
                        chat.Save(kvDataService);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Migration warning for chat {Guid}: {Message}", guid, ex.Message);
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
			if (message.Order < 0)
			{
				// 存在未迁移的老消息（Order < 0）时先整体补号，
				// 否则新消息会和它们撞在同一个序号上，顺序就失去意义了。
				if (Messages.Any(m => m.Order < 0))
					NormalizeOrder();
				message.Order = NextOrder();
			}
			Messages.Add(message);
		}

		/// <summary>
		/// 在指定位置插入消息。插入后按集合顺序重编号并全量持久化——
		/// 顺序权威来自 <see cref="Messages"/>，因此插入必然是一次全量重写（低频操作）。
		/// </summary>
		public void InsertMessage(IMessageStoreService messageStore, int index, Message message)
		{
			if (index < 0 || index > Messages.Count)
				throw new ArgumentOutOfRangeException(nameof(index), index,
					$"Insert index must be between 0 and {Messages.Count} for chat '{Guid}'.");

			Messages.Insert(index, message);
			NormalizeOrder();
			SaveAllMessages(messageStore);
		}

		/// <summary>按当前集合顺序重编号为 0..N-1。</summary>
		public void NormalizeOrder()
		{
			for (int i = 0; i < Messages.Count; i++)
				Messages[i].Order = i;
		}

		/// <summary>
		/// 校验顺序号：全部已分配且等于集合索引。返回 null 表示正常，
		/// 否则返回问题描述。仅供宿主记录告警，不自动修复（自动重排会掩盖裸集合操作）。
		/// </summary>
		public string? ValidateOrder()
		{
			for (int i = 0; i < Messages.Count; i++)
			{
				var m = Messages[i];
				if (m.Order < 0)
					return $"message #{i} (id {m.Id}) has no order assigned";
				if (m.Order != i)
					return $"message #{i} (id {m.Id}) has order {m.Order}, expected {i}";
			}
			return null;
		}

		private long NextOrder()
		{
			long max = -1;
			foreach (var m in Messages)
				if (m.Order > max) max = m.Order;
			return max + 1;
		}

		public void LoadAllMessages(IMessageStoreService messageStore)
		{
			if (Messages == null)
				Messages = new ObservableCollection<Message>();
			else
				Messages.Clear();

			MessageCount = messageStore.GetMessageCount(Guid);
			var loaded = messageStore.GetMessages(Guid, 0, MessageCount);
			foreach (var msg in loaded)
				Messages.Add(msg);

			LastMessagePreview = messageStore.GetLastMessagePreview(Guid) ?? string.Empty;

			// 懒迁移：Order 出现前写入的数据没有顺序号，store 已按其 timestamp 还原出顺序，
			// 这里在内存中补号并全量回写一次。此后该会话一律按 Order 排序。
			if (Messages.Any(m => m.Order < 0))
			{
				NormalizeOrder();
				SaveAllMessages(messageStore);
				_logger?.LogInformation(
					"Migrated message order for chat {ChatGuid} ({Count} messages)", Guid, Messages.Count);
			}
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
