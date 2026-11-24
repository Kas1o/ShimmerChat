using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons.Storage
{
    /// <summary>
    /// 调试输出仓库的双向迁移服务。目标仓库先清空再按原 EntryId 导入，
    /// 保证重复执行不会产生重复条目；任一条目写入失败都会向上抛出，不写迁移标记。
    /// </summary>
    public sealed class DebugOutputMigrationService : IDebugOutputMigrationService
    {
        private readonly FileSystemDebugOutputService _fileStore;
        private readonly LiteDBDebugOutputService _liteStore;
        private readonly ILogger<DebugOutputMigrationService> _logger;

        public DebugOutputMigrationService(
            FileSystemDebugOutputService fileStore,
            LiteDBDebugOutputService liteStore,
            ILogger<DebugOutputMigrationService> logger)
        {
            _fileStore = fileStore;
            _liteStore = liteStore;
            _logger = logger;
        }

        public int MigrateFileToLite(bool clearSource = false)
        {
            var entries = _fileStore.ReadAll();

            _liteStore.ClearAll();
            foreach (var entry in entries)
                _liteStore.Import(entry);

            if (clearSource)
                _fileStore.ClearAll();

            _logger.LogInformation("Migrated {Count} debug output entries from File to LiteDB", entries.Count);
            return entries.Count;
        }

        public int MigrateLiteToFile(bool clearSource = false)
        {
            var entries = _liteStore.ReadAll();

            _fileStore.ClearAll();
            foreach (var entry in entries)
                _fileStore.Import(entry);

            if (clearSource)
                _liteStore.ClearAll();

            _logger.LogInformation("Migrated {Count} debug output entries from LiteDB to File", entries.Count);
            return entries.Count;
        }

        public int GetFileStoreCount() => _fileStore.GetCount();

        public int GetLiteStoreCount() => _liteStore.GetCount();
    }
}
