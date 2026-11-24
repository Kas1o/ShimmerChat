using ShimmerChatLib.Models;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 存储后端迁移标记管理器。每个数据仓库独立记录，互不干扰。
    /// </summary>
    public interface IStorageMigrationMarker
    {
        /// <summary>指定仓库是否已完成过该方向的迁移。</summary>
        bool IsMigrationCompleted(string store, KVStorageType sourceType, KVStorageType targetType);

        /// <summary>记录一个仓库的迁移完成（同仓库同方向会覆盖旧记录）。</summary>
        void MarkMigrationCompleted(
            string store,
            KVStorageType sourceType,
            KVStorageType targetType,
            int migratedCount,
            bool sourceCleared);

        /// <summary>读取全部迁移记录。</summary>
        IReadOnlyList<StorageMigrationRecord> GetRecords();

        /// <summary>清除全部迁移记录（含旧版标记文件）。</summary>
        void ClearMarker();
    }
}
