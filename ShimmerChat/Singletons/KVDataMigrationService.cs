using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// KV 数据迁移服务
    /// </summary>
    public class KVDataMigrationService : IKVDataMigrationService
    {
        private readonly LocalFileStorageKVData _localFileStorage;
        private readonly LiteDBKVData _liteDBStorage;
        private readonly ILogger<KVDataMigrationService> _logger;

        /// <summary>
        /// 初始化 KVDataMigrationService 实例
        /// </summary>
        /// <param name="localFileStorage">本地文件存储实例</param>
        /// <param name="liteDBStorage">LiteDB 存储实例</param>
        public KVDataMigrationService(
            LocalFileStorageKVData localFileStorage,
            LiteDBKVData liteDBStorage,
            ILogger<KVDataMigrationService> logger)
        {
            _localFileStorage = localFileStorage;
            _liteDBStorage = liteDBStorage;
            _logger = logger;
        }

        /// <summary>
        /// 从本地文件存储迁移到 LiteDB
        /// </summary>
        /// <param name="clearSource">迁移完成后是否清空源数据</param>
        /// <returns>迁移的条目数量</returns>
        public int MigrateToLiteDB(bool clearSource = false)
        {
            int count = 0;
            var entries = new List<LiteDBKVData.KVDataEntry>();

            foreach (var spaceId in _localFileStorage.GetAllSpaceIds())
            {
                foreach (var item in _localFileStorage.GetAllEntries(spaceId))
                {
                    entries.Add(new LiteDBKVData.KVDataEntry
                    {
                        SpaceId = item.SpaceId,
                        Key = item.Key,
                        Value = item.Value
                    });
                    count++;
                }
            }

            if (entries.Count > 0)
            {
                _liteDBStorage.BulkWrite(entries);
            }

            if (clearSource)
            {
                _localFileStorage.ClearAll();
            }

            _logger.LogInformation("Migrated {Count} entries from LocalFileStorage to LiteDB", count);
            return count;
        }

        /// <summary>
        /// 从 LiteDB 迁移到本地文件存储
        /// </summary>
        /// <param name="clearSource">迁移完成后是否清空源数据</param>
        /// <returns>迁移的条目数量</returns>
        public int MigrateToLocalFileStorage(bool clearSource = false)
        {
            int count = 0;

            foreach (var spaceId in _liteDBStorage.GetAllSpaceIds())
            {
                foreach (var entry in _liteDBStorage.GetAllEntries(spaceId))
                {
                    _localFileStorage.Write(entry.SpaceId, entry.Key, entry.Value);
                    count++;
                }
            }

            if (clearSource)
            {
                _liteDBStorage.ClearAll();
            }

            _logger.LogInformation("Migrated {Count} entries from LiteDB to LocalFileStorage", count);
            return count;
        }

        /// <summary>
        /// 双向同步 - 合并两个存储中的数据（以较新的为准，此处简单合并）
        /// </summary>
        /// <returns>同步的条目数量</returns>
        public int SyncStorages()
        {
            int count = 0;

            // 从 LocalFileStorage 同步到 LiteDB
            foreach (var spaceId in _localFileStorage.GetAllSpaceIds())
            {
                foreach (var item in _localFileStorage.GetAllEntries(spaceId))
                {
                    var existing = _liteDBStorage.Read(item.SpaceId, item.Key);
                    if (existing == null)
                    {
                        _liteDBStorage.Write(item.SpaceId, item.Key, item.Value);
                        count++;
                    }
                }
            }

            // 从 LiteDB 同步到 LocalFileStorage
            foreach (var spaceId in _liteDBStorage.GetAllSpaceIds())
            {
                foreach (var entry in _liteDBStorage.GetAllEntries(spaceId))
                {
                    var existing = _localFileStorage.Read(entry.SpaceId, entry.Key);
                    if (existing == null)
                    {
                        _localFileStorage.Write(entry.SpaceId, entry.Key, entry.Value);
                        count++;
                    }
                }
            }

            _logger.LogInformation("Synced {Count} entries between storages", count);
            return count;
        }

        /// <summary>
        /// 获取本地文件存储中的条目数量
        /// </summary>
        /// <returns>条目数量</returns>
        public int GetLocalFileStorageCount()
        {
            int count = 0;
            foreach (var spaceId in _localFileStorage.GetAllSpaceIds())
            {
                count += _localFileStorage.GetAllKeys(spaceId).Count();
            }
            return count;
        }

        /// <summary>
        /// 获取 LiteDB 中的条目数量
        /// </summary>
        /// <returns>条目数量</returns>
        public int GetLiteDBCount()
        {
            int count = 0;
            foreach (var spaceId in _liteDBStorage.GetAllSpaceIds())
            {
                count += _liteDBStorage.GetAllKeys(spaceId).Count();
            }
            return count;
        }
    }
}
