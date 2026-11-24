namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 一条存储后端迁移记录（按数据仓库区分：KVData / ChatMessage / DebugOutput）。
    /// </summary>
    public sealed class StorageMigrationRecord
    {
        /// <summary>数据仓库标识，见 <see cref="StorageStores"/>。</summary>
        public string Store { get; set; } = string.Empty;

        public string SourceStorage { get; set; } = string.Empty;

        public string TargetStorage { get; set; } = string.Empty;

        public int MigratedCount { get; set; }

        public bool SourceCleared { get; set; }

        public DateTime MigrationTime { get; set; }
    }

    /// <summary>
    /// 数据仓库标识。迁移标记、配置读取与启动流程共用这一份名字，避免手写字符串漂移。
    /// </summary>
    public static class StorageStores
    {
        public const string KvData = "KVData";
        public const string ChatMessage = "ChatMessage";
        public const string DebugOutput = "DebugOutput";
        public const string Vector = "VectorStore";
    }
}
