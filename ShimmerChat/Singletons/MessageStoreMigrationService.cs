using Microsoft.Extensions.Logging;
using ShimmerChatLib;
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
        private readonly ILogger<MessageStoreMigrationService> _logger;

        public MessageStoreMigrationService(
            FileMessageStoreService fileStore,
            LiteDBMessageStoreService liteStore,
            ILogger<MessageStoreMigrationService> logger)
        {
            _fileStore = fileStore;
            _liteStore = liteStore;
            _logger = logger;
        }

        public int MigrateFileToLite()
        {
            int count = 0;
            foreach (var chatGuid in _fileStore.GetAllChatGuids())
            {
                if (_fileStore.GetMessageCount(chatGuid) == 0) continue;

                int offset = 0;
                const int batchSize = 100;

                while (true)
                {
                    List<Message> messages;
                    try
                    {
                        messages = _fileStore.GetMessages(chatGuid, offset, batchSize);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to read messages for chat {ChatGuid} at offset {Offset}",
                            chatGuid, offset);
                        // 读取失败立即中止：异常向上传播 → 不写迁移标记 → 下次启动重试
                        throw new InvalidOperationException(
                            $"Failed to read messages for chat {chatGuid} at offset {offset}", ex);
                    }

                    if (messages.Count == 0) break;

                    // 确认源数据可读后再清空目标，避免读源失败时破坏目标存储
                    if (offset == 0)
                    {
                        _liteStore.DeleteAllMessages(chatGuid);
                    }

                    foreach (var msg in messages)
                    {
                        try
                        {
                            _liteStore.InsertMessage(chatGuid, msg);
                            count++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to write message {MessageId} to LiteDB",
                                msg.Id);
                            // 写入失败立即中止：异常向上传播 → 不写迁移标记 → 下次启动重试
                            throw new InvalidOperationException(
                                $"Failed to write message {msg.Id} of chat {chatGuid} to LiteDB", ex);
                        }
                    }

                    offset += messages.Count;
                }
            }
            _logger.LogInformation("Migrated {Count} messages from File to LiteDB", count);
            return count;
        }

        public int MigrateLiteToFile()
        {
            int count = 0;
            foreach (var chatGuid in _liteStore.GetAllChatGuids())
            {
                if (_liteStore.GetMessageCount(chatGuid) == 0) continue;

                int offset = 0;
                const int batchSize = 100;

                while (true)
                {
                    List<Message> messages;
                    try
                    {
                        messages = _liteStore.GetMessages(chatGuid, offset, batchSize);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to read messages for chat {ChatGuid} at offset {Offset}",
                            chatGuid, offset);
                        // 读取失败立即中止：异常向上传播 → 不写迁移标记 → 下次启动重试
                        throw new InvalidOperationException(
                            $"Failed to read messages for chat {chatGuid} at offset {offset}", ex);
                    }

                    if (messages.Count == 0) break;

                    // 确认源数据可读后再清空目标，避免读源失败时破坏目标存储
                    if (offset == 0)
                    {
                        _fileStore.DeleteAllMessages(chatGuid);
                    }

                    foreach (var msg in messages)
                    {
                        try
                        {
                            _fileStore.InsertMessage(chatGuid, msg);
                            count++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to write message {MessageId} to file storage",
                                msg.Id);
                            // 写入失败立即中止：异常向上传播 → 不写迁移标记 → 下次启动重试
                            throw new InvalidOperationException(
                                $"Failed to write message {msg.Id} of chat {chatGuid} to file storage", ex);
                        }
                    }

                    offset += messages.Count;
                }
            }
            _logger.LogInformation("Migrated {Count} messages from LiteDB to File", count);
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
