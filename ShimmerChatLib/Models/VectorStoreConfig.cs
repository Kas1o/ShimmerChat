using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 向量存储配置。数据位置由 <see cref="Storage.DataPaths"/> 统一决定，不再单独配置根目录。
    /// </summary>
    public class VectorStoreConfig
    {
        /// <summary>
        /// 实现类型：LiteDb、File 或 InMemory。
        /// </summary>
        public string Provider { get; set; } = "LiteDb";

        /// <summary>
        /// 解析实现类型。未知取值直接抛错，不静默回落到默认实现。
        /// </summary>
        public VectorStoreProvider GetProvider()
        {
            return Provider?.Trim().ToLowerInvariant() switch
            {
                "litedb" or "lite_db" or "lite" => VectorStoreProvider.LiteDb,
                "file" or "local" or "localfilestorage" or "local_file_storage" => VectorStoreProvider.File,
                "inmemory" or "in_memory" => VectorStoreProvider.InMemory,
                _ => throw new InvalidOperationException(
                    $"VectorStore:Provider 取值 '{Provider}' 非法，只支持 LiteDb、File 或 InMemory。")
            };
        }
    }
}
