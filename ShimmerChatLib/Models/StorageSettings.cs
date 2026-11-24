namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 存储配置：每个数据仓库（KV / 聊天消息 / 调试输出 / 向量）独立选择后端。
    /// 唯一事实来源是 <c>Data/storage_settings.json</c>。
    /// </summary>
    public sealed class StorageSettings
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>配置格式版本。低版本由加载方迁移，不静默忽略。</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>KV 数据仓库（Agent、Chat、设置、记忆文本等）。</summary>
        public StoreStorageConfig KVData { get; set; } = new();

        /// <summary>聊天消息仓库。</summary>
        public StoreStorageConfig ChatMessage { get; set; } = new();

        /// <summary>调试输出仓库。</summary>
        public StoreStorageConfig DebugOutput { get; set; } = new();

        /// <summary>向量仓库（LiteDb / File / InMemory）。</summary>
        public VectorStoreConfig VectorStore { get; set; } = new();

        /// <summary>
        /// 生成出厂默认配置。旧版 appsettings 的全局 KVDataStorage 配置会被复制到三个仓库，
        /// 保证升级后行为与旧版一致；各仓库此后独立修改。
        /// </summary>
        public static StorageSettings CreateDefaults(StoreStorageConfig? legacyStore, VectorStoreConfig? legacyVectorStore)
        {
            var seed = legacyStore?.Clone() ?? new StoreStorageConfig();

            return new StorageSettings
            {
                KVData = seed,
                ChatMessage = seed.Clone(),
                DebugOutput = seed.Clone(),
                VectorStore = legacyVectorStore ?? new VectorStoreConfig()
            };
        }
    }
}
