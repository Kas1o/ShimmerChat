using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 向量存储配置。appsettings.json 只提供出厂默认值，UI 覆盖配置在后续版本接入。
    /// </summary>
    public class VectorStoreConfig
    {
        /// <summary>
        /// 实现类型：LiteDb 或 InMemory。
        /// </summary>
        public string Provider { get; set; } = "LiteDb";

        /// <summary>
        /// 数据根目录。相对路径按应用目录解析。
        /// </summary>
        public string RootPath { get; set; } = "VectorStore";

        /// <summary>
        /// 文件隔离粒度：PerOwner（每个 owner 一个文件）或 PerCollection（每个集合一个文件）。
        /// </summary>
        public string Scope { get; set; } = "PerOwner";

        /// <summary>
        /// 解析实现类型。未知取值直接抛错，不静默回落到默认实现。
        /// </summary>
        public VectorStoreProvider GetProvider()
        {
            return Provider.ToLowerInvariant() switch
            {
                "litedb" or "lite_db" => VectorStoreProvider.LiteDb,
                "inmemory" or "in_memory" => VectorStoreProvider.InMemory,
                _ => throw new InvalidOperationException(
                    $"VectorStore:Provider 取值 '{Provider}' 非法，只支持 LiteDb 或 InMemory。")
            };
        }

        /// <summary>
        /// 是否为"每个集合一个文件"的隔离粒度。
        /// </summary>
        public bool IsPerCollectionScope()
        {
            return Scope.ToLowerInvariant() switch
            {
                "perowner" or "per_owner" => false,
                "percollection" or "per_collection" => true,
                _ => throw new InvalidOperationException(
                    $"VectorStore:Scope 取值 '{Scope}' 非法，只支持 PerOwner 或 PerCollection。")
            };
        }
    }
}
