using LiteDB;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using System;
using System.IO;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// LiteDB 实现的 KV 数据存储服务
    /// </summary>
    public class LiteDBKVData : IKVDataService
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<KVDataEntry> _collection;
        private readonly ILogger<LiteDBKVData> _logger;

        /// <summary>
        /// KV 数据条目实体
        /// </summary>
        public class KVDataEntry
        {
            public ObjectId Id { get; set; } = ObjectId.NewObjectId();
            public string SpaceId { get; set; } = string.Empty;
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        /// <summary>
        /// 初始化 LiteDBKVData 实例
        /// </summary>
        public LiteDBKVData(LiteDatabase database, ILogger<LiteDBKVData> logger)
        {
            _database = database;
            _logger = logger;
            _collection = _database.GetCollection<KVDataEntry>("kvdata");
            _collection.EnsureIndex(x => x.SpaceId);
            _collection.EnsureIndex(x => new { x.SpaceId, x.Key }, true);
        }

        /// <summary>
        /// 读取 KV 数据
        /// </summary>
        /// <param name="spaceId">空间 ID</param>
        /// <param name="key">数据键名</param>
        /// <returns>存储的数据，如果不存在则返回 null</returns>
        public string? Read(string spaceId, string key)
        {
            if (string.IsNullOrWhiteSpace(spaceId))
                throw new ArgumentNullException(nameof(spaceId), "Space ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");

            try
            {
                var entry = _collection.FindOne(x => x.SpaceId == spaceId && x.Key == key);
                return entry?.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from LiteDB: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        /// <param name="spaceId">空间 ID</param>
        /// <param name="key">数据键名</param>
        /// <param name="value">要存储的数据值</param>
        public void Write(string spaceId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(spaceId))
                throw new ArgumentNullException(nameof(spaceId), "Space ID cannot be null or whitespace");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key), "Key cannot be null or whitespace");
            if (value == null)
                throw new ArgumentNullException(nameof(value), "Value cannot be null");

            try
            {
                var existing = _collection.FindOne(x => x.SpaceId == spaceId && x.Key == key);
                if (existing != null)
                {
                    existing.Value = value;
                    _collection.Update(existing);
                }
                else
                {
                    _collection.Insert(new KVDataEntry
                    {
                        SpaceId = spaceId,
                        Key = key,
                        Value = value
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to LiteDB: {Message}", ex.Message);
                throw new IOException("Failed to write data to LiteDB", ex);
            }
        }

        /// <summary>
        /// 获取所有空间 ID 列表
        /// </summary>
        /// <returns>空间 ID 列表</returns>
        public IEnumerable<string> GetAllSpaceIds()
        {
            return _collection.Query()
                .Select(x => x.SpaceId)
                .ToList()
                .Distinct();
        }

        /// <summary>
        /// 获取指定空间下的所有键
        /// </summary>
        /// <param name="spaceId">空间 ID</param>
        /// <returns>键列表</returns>
        public IEnumerable<string> GetAllKeys(string spaceId)
        {
            return _collection.Query()
                .Where(x => x.SpaceId == spaceId)
                .Select(x => x.Key)
                .ToList();
        }

        /// <summary>
        /// 获取指定空间下的所有条目
        /// </summary>
        /// <param name="spaceId">空间 ID</param>
        /// <returns>条目列表</returns>
        public IEnumerable<KVDataEntry> GetAllEntries(string spaceId)
        {
            return _collection.Find(x => x.SpaceId == spaceId);
        }

        /// <summary>
        /// 批量写入条目
        /// </summary>
        /// <param name="entries">条目列表</param>
        public void BulkWrite(IEnumerable<KVDataEntry> entries)
        {
            _database.BeginTrans();
            try
            {
                foreach (var entry in entries)
                {
                    var existing = _collection.FindOne(x => x.SpaceId == entry.SpaceId && x.Key == entry.Key);
                    if (existing != null)
                    {
                        existing.Value = entry.Value;
                        _collection.Update(existing);
                    }
                    else
                    {
                        _collection.Insert(entry);
                    }
                }
                _database.Commit();
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public void ClearAll()
        {
            _collection.DeleteAll();
        }

    }
}
