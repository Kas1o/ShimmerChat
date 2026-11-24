namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 单个数据仓库的存储配置。KV、聊天消息、调试输出各有一份，互不影响。
    /// </summary>
    public class StoreStorageConfig
    {
        /// <summary>
        /// 存储类型：LocalFileStorage 或 LiteDB。
        /// </summary>
        public string StorageType { get; set; } = "LiteDB";

        /// <summary>
        /// 是否在启动时自动迁移数据。
        /// </summary>
        public bool AutoMigrateOnStartup { get; set; } = true;

        /// <summary>
        /// 自动迁移的源存储类型（从哪个存储迁移到目标存储）；为空表示不自动迁移。
        /// </summary>
        public string? AutoMigrateFrom { get; set; } = "LocalFileStorage";

        /// <summary>
        /// 迁移后是否清空源数据。
        /// </summary>
        public bool ClearSourceAfterMigration { get; set; }

        /// <summary>
        /// 强制迁移模式（即使已经有迁移标记也执行迁移），需要 CLI 确认。
        /// </summary>
        public bool ForceMigration { get; set; }

        /// <summary>
        /// 解析存储类型。未知取值直接抛错，不回落到默认实现。
        /// </summary>
        public KVStorageType GetStorageType()
        {
            return StorageType?.Trim().ToLowerInvariant() switch
            {
                "litedb" or "lite_db" or "lite" => KVStorageType.LiteDB,
                "localfilestorage" or "local_file_storage" or "local" or "file" => KVStorageType.LocalFileStorage,
                _ => throw new InvalidOperationException(
                    $"存储类型 '{StorageType}' 非法，只支持 LiteDB 或 LocalFileStorage。")
            };
        }

        /// <summary>
        /// 解析自动迁移源类型。未设置返回 null；非法取值直接抛错。
        /// </summary>
        public KVStorageType? GetAutoMigrateFromType()
        {
            if (string.IsNullOrWhiteSpace(AutoMigrateFrom))
                return null;

            return AutoMigrateFrom.Trim().ToLowerInvariant() switch
            {
                "litedb" or "lite_db" or "lite" => KVStorageType.LiteDB,
                "localfilestorage" or "local_file_storage" or "local" or "file" => KVStorageType.LocalFileStorage,
                _ => throw new InvalidOperationException(
                    $"AutoMigrateFrom 取值 '{AutoMigrateFrom}' 非法，只支持 LiteDB、LocalFileStorage 或 null。")
            };
        }

        /// <summary>复制一份配置（三个仓库各自持有独立实例，避免互相影响）。</summary>
        public StoreStorageConfig Clone() => (StoreStorageConfig)MemberwiseClone();
    }

    /// <summary>
    /// 旧版全局 KV 存储配置。保留类型名只为兼容既有调用方；
    /// 新代码一律使用 <see cref="StoreStorageConfig"/>。
    /// </summary>
    public class KVDataStorageConfig : StoreStorageConfig
    {
    }
}
