namespace ShimmerChat.Singletons
{
    /// <summary>
    /// KV 数据存储配置
    /// </summary>
    public class KVDataStorageConfig
    {
        /// <summary>
        /// 存储类型：LocalFileStorage 或 LiteDB
        /// </summary>
        public string StorageType { get; set; } = "LiteDB";

        /// <summary>
        /// 是否在启动时自动迁移数据
        /// </summary>
        public bool AutoMigrateOnStartup { get; set; } = true;

        /// <summary>
        /// 自动迁移的源存储类型（从哪个存储迁移到目标存储）
        /// </summary>
        public string? AutoMigrateFrom { get; set; } = "LocalFileStorage";

        /// <summary>
        /// 迁移后是否清空源数据
        /// </summary>
        public bool ClearSourceAfterMigration { get; set; } = false;

        /// <summary>
        /// 获取存储类型枚举
        /// </summary>
        /// <returns>存储类型枚举值</returns>
        public KVStorageType GetStorageType()
        {
            return StorageType.ToLowerInvariant() switch
            {
                "litedb" or "lite_db" => KVStorageType.LiteDB,
                _ => KVStorageType.LocalFileStorage
            };
        }

        /// <summary>
        /// 获取自动迁移源存储类型枚举
        /// </summary>
        /// <returns>存储类型枚举值，如果未设置则返回 null</returns>
        public KVStorageType? GetAutoMigrateFromType()
        {
            if (string.IsNullOrEmpty(AutoMigrateFrom))
                return null;

            return AutoMigrateFrom.ToLowerInvariant() switch
            {
                "litedb" or "lite_db" => KVStorageType.LiteDB,
                "localfilestorage" or "local_file_storage" => KVStorageType.LocalFileStorage,
                _ => null
            };
        }
    }
}
