using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 消息存储双向迁移服务
    /// </summary>
    public class MessageStoreMigrationService : IMessageStoreMigrationService
    {
        private readonly FileMessageStoreService _fileStore;
        private readonly LiteDBMessageStoreService _liteStore;

        public MessageStoreMigrationService(
            FileMessageStoreService fileStore,
            LiteDBMessageStoreService liteStore)
        {
            _fileStore = fileStore;
            _liteStore = liteStore;
        }

        public int MigrateFileToLite()
        {
            int count = 0;
            foreach (var chatGuid in _fileStore.GetAllChatGuids())
            {
                if (_fileStore.GetMessageCount(chatGuid) == 0) continue;

                _liteStore.DeleteAllMessages(chatGuid);
                int offset = 0;
                const int batchSize = 100;

                while (true)
                {
                    var messages = _fileStore.GetMessages(chatGuid, offset, batchSize);
                    if (messages.Count == 0) break;

                    foreach (var msg in messages)
                        _liteStore.InsertMessage(chatGuid, msg);

                    offset += messages.Count;
                    count += messages.Count;
                }
            }
            return count;
        }

        public int MigrateLiteToFile()
        {
            int count = 0;
            foreach (var chatGuid in _liteStore.GetAllChatGuids())
            {
                if (_liteStore.GetMessageCount(chatGuid) == 0) continue;

                _fileStore.DeleteAllMessages(chatGuid);
                int offset = 0;
                const int batchSize = 100;

                while (true)
                {
                    var messages = _liteStore.GetMessages(chatGuid, offset, batchSize);
                    if (messages.Count == 0) break;

                    foreach (var msg in messages)
                        _fileStore.InsertMessage(chatGuid, msg);

                    offset += messages.Count;
                    count += messages.Count;
                }
            }
            return count;
        }

        public int GetFileStoreCount()
        {
            int count = 0;
            foreach (var chatGuid in _fileStore.GetAllChatGuids())
                count += _fileStore.GetMessageCount(chatGuid);
            return count;
        }

        public int GetLiteStoreCount()
        {
            int count = 0;
            foreach (var chatGuid in _liteStore.GetAllChatGuids())
                count += _liteStore.GetMessageCount(chatGuid);
            return count;
        }
    }
}
