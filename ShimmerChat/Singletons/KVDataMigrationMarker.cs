using System;
using System.IO;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// KV 数据迁移标记管理器
    /// 用于记录和检查迁移状态，防止重复迁移
    /// </summary>
    public class KVDataMigrationMarker : IKVDataMigrationMarker
    {
        private readonly string _markerFilePath;
        private const string MarkerFileName = ".migration_marker";

        /// <summary>
        /// 初始化迁移标记管理器
        /// </summary>
        public KVDataMigrationMarker()
        {
            string kvDataPath = Path.Combine(AppContext.BaseDirectory, "KVData");
            _markerFilePath = Path.Combine(kvDataPath, MarkerFileName);
        }

        /// <summary>
        /// 检查是否已经完成过指定方向的迁移
        /// </summary>
        /// <param name="sourceType">源存储类型</param>
        /// <param name="targetType">目标存储类型</param>
        /// <returns>如果已完成过相同方向的迁移返回 true</returns>
        public bool IsMigrationCompleted(KVStorageType sourceType, KVStorageType targetType)
        {
            if (!File.Exists(_markerFilePath))
                return false;

            try
            {
                string json = File.ReadAllText(_markerFilePath);
                var marker = JsonConvert.DeserializeObject<MigrationMarkerInfo>(json);

                if (marker == null)
                    return false;

                return marker.SourceStorage.Equals(sourceType.ToString(), StringComparison.OrdinalIgnoreCase)
                    && marker.TargetStorage.Equals(targetType.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 记录迁移完成
        /// </summary>
        /// <param name="sourceType">源存储类型</param>
        /// <param name="targetType">目标存储类型</param>
        /// <param name="migratedCount">迁移的条目数量</param>
        /// <param name="sourceCleared">是否清空了源数据</param>
        public void MarkMigrationCompleted(KVStorageType sourceType, KVStorageType targetType, int migratedCount, bool sourceCleared)
        {
            string? directory = Path.GetDirectoryName(_markerFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var marker = new MigrationMarkerInfo
            {
                MigrationTime = DateTime.UtcNow,
                SourceStorage = sourceType.ToString(),
                TargetStorage = targetType.ToString(),
                MigratedCount = migratedCount,
                SourceCleared = sourceCleared
            };

            string json = JsonConvert.SerializeObject(marker, Formatting.Indented);
            File.WriteAllText(_markerFilePath, json);
        }

        /// <summary>
        /// 清除迁移标记
        /// </summary>
        public void ClearMarker()
        {
            if (File.Exists(_markerFilePath))
            {
                File.Delete(_markerFilePath);
            }
        }

        /// <summary>
        /// 读取迁移标记信息
        /// </summary>
        /// <returns>迁移标记信息，如果不存在返回 null</returns>
        public MigrationMarkerInfo? GetMarkerInfo()
        {
            if (!File.Exists(_markerFilePath))
                return null;

            try
            {
                string json = File.ReadAllText(_markerFilePath);
                return JsonConvert.DeserializeObject<MigrationMarkerInfo>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
