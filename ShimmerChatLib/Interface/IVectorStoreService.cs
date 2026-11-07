using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 向量距离度量。所有实现的 <see cref="VectorHit.Score"/> 统一为"越大越相似"。
    /// </summary>
    public enum VectorMetric
    {
        /// <summary>余弦相似度。写入与查询时都会做 L2 归一化。</summary>
        Cosine = 0,

        /// <summary>欧氏距离。对外分数为 1 / (1 + distance)。</summary>
        Euclidean = 1,

        /// <summary>内积。不做归一化。</summary>
        DotProduct = 2
    }

    /// <summary>
    /// 向量落盘编码。当前仅支持 float32，其余取值会在创建集合时明确失败。
    /// </summary>
    public enum VectorEncoding
    {
        Float32 = 0,
        Float16 = 1
    }

    /// <summary>
    /// 向量存储实现类型。
    /// </summary>
    public enum VectorStoreProvider
    {
        LiteDb = 0,
        InMemory = 1
    }

    /// <summary>
    /// 集合元数据。维度/度量/模型不一致时写入必须失败，不允许静默混用。
    /// </summary>
    public sealed record CollectionOptions(
        int Dimensions,
        VectorMetric Metric = VectorMetric.Cosine,
        string? EmbeddingModel = null,
        string? Owner = null,
        VectorEncoding Encoding = VectorEncoding.Float32);

    /// <summary>
    /// 集合信息。
    /// </summary>
    public sealed record CollectionInfo(string Name, CollectionOptions Options, int Count);

    /// <summary>
    /// 一条待写入向量。Partition 用于集合内分区（例如 AgentId），过滤时无需全量扫描。
    /// </summary>
    public sealed record VectorRecord(
        string Id,
        ReadOnlyMemory<float> Vector,
        string? Partition = null,
        DateTimeOffset CreatedAt = default,
        IReadOnlyDictionary<string, string>? Payload = null);

    /// <summary>
    /// 一条检索结果。
    /// </summary>
    public sealed record VectorHit(
        string Id,
        float Score,
        string? Partition,
        DateTimeOffset CreatedAt,
        IReadOnlyDictionary<string, string>? Payload);

    /// <summary>
    /// 向量记录的精简元数据，用于索引对账（不返回向量本体）。
    /// </summary>
    public sealed record VectorRecordMeta(
        string Id,
        string? Partition,
        IReadOnlyDictionary<string, string>? Payload);

    /// <summary>
    /// 检索过滤条件。Tags 与 payload 中的 "tags"（逗号分隔）做"全部包含"匹配。
    /// </summary>
    public sealed record VectorFilter(
        string? Partition = null,
        IReadOnlyCollection<string>? Tags = null,
        DateTimeOffset? From = null,
        DateTimeOffset? To = null,
        IReadOnlyCollection<string>? Ids = null);

    /// <summary>
    /// 存储健康状态。
    /// </summary>
    public sealed record VectorStoreHealth(bool Healthy, string ProviderId, string? Message = null);

    /// <summary>
    /// 嵌入式向量存储服务。契约只描述"集合 + 记录 + 检索"，不包含任何记忆领域概念。
    /// </summary>
    public interface IVectorStoreService : IAsyncDisposable
    {
        /// <summary>当前实现标识，用于诊断与 UI 展示。</summary>
        string ProviderId { get; }

        /// <summary>
        /// 存储实例身份（实现 + 数据位置）。用于判断持久化索引状态是否仍属于当前后端；
        /// 切换 Provider 或 RootPath 后必须视为未就绪，而不是沿用旧状态。
        /// </summary>
        string StoreIdentity { get; }

        /// <summary>创建集合；已存在时校验维度/度量/模型是否一致。</summary>
        Task EnsureCollectionAsync(string collection, CollectionOptions options, CancellationToken ct = default);

        /// <summary>列出所有集合。</summary>
        Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken ct = default);

        /// <summary>删除集合及其全部记录。</summary>
        Task DropCollectionAsync(string collection, CancellationToken ct = default);

        /// <summary>列出记录元数据（不含向量），用于索引对账。</summary>
        Task<IReadOnlyList<VectorRecordMeta>> ListRecordsAsync(
            string collection,
            string? partition = null,
            CancellationToken ct = default);

        /// <summary>批量写入（按 Id 覆盖）。集合必须已存在。</summary>
        Task UpsertAsync(string collection, IReadOnlyList<VectorRecord> records, CancellationToken ct = default);

        /// <summary>按条件删除。空条件会被拒绝，避免误删整库。</summary>
        Task DeleteAsync(string collection, VectorFilter filter, CancellationToken ct = default);

        /// <summary>单条查询检索。</summary>
        Task<IReadOnlyList<VectorHit>> SearchAsync(
            string collection,
            ReadOnlyMemory<float> query,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default);

        /// <summary>
        /// 批量查询检索。多次查询共享一次索引遍历，返回顺序与 queries 一一对应。
        /// </summary>
        Task<IReadOnlyList<IReadOnlyList<VectorHit>>> SearchBatchAsync(
            string collection,
            IReadOnlyList<ReadOnlyMemory<float>> queries,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default);

        /// <summary>健康检查。</summary>
        Task<VectorStoreHealth> CheckHealthAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 向量存储错误码。
    /// </summary>
    public static class VectorStoreErrorCodes
    {
        public const string CollectionNotFound = "VECTOR_COLLECTION_NOT_FOUND";
        public const string CollectionExists = "VECTOR_COLLECTION_EXISTS";
        public const string DimensionMismatch = "VECTOR_DIMENSION_MISMATCH";
        public const string MetricMismatch = "VECTOR_METRIC_MISMATCH";
        public const string ModelMismatch = "VECTOR_MODEL_MISMATCH";
        public const string UnsupportedEncoding = "VECTOR_UNSUPPORTED_ENCODING";
        public const string InvalidArgument = "VECTOR_INVALID_ARGUMENT";
        public const string StorageFailure = "VECTOR_STORAGE_FAILURE";
    }

    /// <summary>
    /// 向量存储领域异常，携带稳定错误码。
    /// </summary>
    public sealed class VectorStoreException : Exception
    {
        public VectorStoreException(string code, string message, Exception? inner = null)
            : base(message, inner)
        {
            Code = code;
        }

        public string Code { get; }
    }

    /// <summary>
    /// 契约层公共校验。
    /// </summary>
    public static class VectorStoreGuard
    {
        private static readonly Regex CollectionNamePattern = new(
            "^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>集合名白名单，禁止路径分隔符与 ".."。</summary>
        public static void ValidateCollectionName(string collection)
        {
            if (string.IsNullOrWhiteSpace(collection))
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "集合名不能为空。");
            if (!CollectionNamePattern.IsMatch(collection) || collection.Contains("..", StringComparison.Ordinal))
                throw new VectorStoreException(
                    VectorStoreErrorCodes.InvalidArgument,
                    $"集合名 '{collection}' 非法：只允许字母、数字、'_'、'.'、'-'、':'，且不能包含 '..'。");
        }

        /// <summary>校验集合选项。</summary>
        public static void ValidateOptions(CollectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.Dimensions <= 0)
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "Dimensions 必须大于 0。");
            if (options.Encoding != VectorEncoding.Float32)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.UnsupportedEncoding,
                    $"编码 {options.Encoding} 尚未实现，当前只支持 Float32。");
        }

        /// <summary>校验已存在集合与请求选项是否兼容；不兼容时给出稳定错误码。</summary>
        public static void ValidateCompatible(CollectionOptions existing, CollectionOptions requested)
        {
            if (existing.Dimensions != requested.Dimensions)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.DimensionMismatch,
                    $"集合维度为 {existing.Dimensions}，请求为 {requested.Dimensions}。");

            if (existing.Metric != requested.Metric)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.MetricMismatch,
                    $"集合度量为 {existing.Metric}，请求为 {requested.Metric}。");

            if (existing.Encoding != requested.Encoding)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.UnsupportedEncoding,
                    $"集合编码为 {existing.Encoding}，请求为 {requested.Encoding}。");

            if (!string.IsNullOrEmpty(existing.EmbeddingModel) &&
                !string.IsNullOrEmpty(requested.EmbeddingModel) &&
                !string.Equals(existing.EmbeddingModel, requested.EmbeddingModel, StringComparison.Ordinal))
            {
                throw new VectorStoreException(
                    VectorStoreErrorCodes.ModelMismatch,
                    $"集合绑定模型 '{existing.EmbeddingModel}'，请求为 '{requested.EmbeddingModel}'。");
            }
        }

        /// <summary>校验分区名：null 表示不分分区，非 null 时不能为空白。</summary>
        public static void ValidatePartition(string? partition)
        {
            if (partition is not null && string.IsNullOrWhiteSpace(partition))
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "Partition 不能为空白字符串。");
        }
    }
}
