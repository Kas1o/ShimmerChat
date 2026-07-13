using ShimmerChatLib.Models;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// KV 数据迁移标记管理器接口。
    /// </summary>
    public interface IKVDataMigrationMarker
    {
        bool IsMigrationCompleted(KVStorageType sourceType, KVStorageType targetType);
        void MarkMigrationCompleted(KVStorageType sourceType, KVStorageType targetType, int migratedCount, bool sourceCleared);
        void ClearMarker();
        MigrationMarkerInfo? GetMarkerInfo();
    }
}
