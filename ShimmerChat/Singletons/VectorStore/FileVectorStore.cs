using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Singletons.VectorStore
{
    /// <summary>
    /// 文件后端的向量存储：每个集合落盘为 <c>Data/LocalFileStorage/vector/</c> 下的一个 JSON 文件，
    /// 检索仍在进程内索引中完成。
    /// <para>
    /// 写入采用"整文件原子替换"（临时文件 + 覆盖移动），因此一次 Upsert/Delete 的代价与集合大小成正比；
    /// 适合中小规模与便于直接查看/备份的场景，大规模集合应使用 LiteDB 后端。
    /// </para>
    /// </summary>
    public sealed class FileVectorStore : IVectorStoreService, IDisposable
    {
        private const int SchemaVersion = 1;
        private const int MaxNamePrefixLength = 40;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<FileVectorStore> _logger;
        private readonly string _rootPath;

        private readonly object _sync = new();
        private readonly Dictionary<string, CollectionState> _states = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        public FileVectorStore(DataPaths paths, ILogger<FileVectorStore> logger)
        {
            ArgumentNullException.ThrowIfNull(paths);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rootPath = paths.LocalVectorDirectory;
            Directory.CreateDirectory(_rootPath);
        }

        public string ProviderId => "File";

        public string StoreIdentity => $"File:{_rootPath}";

        public async Task EnsureCollectionAsync(string collection, CollectionOptions options, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            VectorStoreGuard.ValidateOptions(options);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    if (TryGetState(collection) is { } existing)
                    {
                        VectorStoreGuard.ValidateCompatible(existing.Options, options);
                        return;
                    }

                    var file = GetFilePath(collection);
                    if (File.Exists(file))
                    {
                        var state = LoadState(file, collection);
                        VectorStoreGuard.ValidateCompatible(state.Options, options);
                        AddState(collection, state);
                        return;
                    }

                    var created = new CollectionState(
                        collection,
                        options,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                    // 先落盘再登记到内存：写失败时不能留下一个"只在内存里存在"的集合。
                    Persist(created);
                    AddState(collection, created);
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            var result = new List<CollectionInfo>();

            string[] files;
            try
            {
                files = Directory.GetFiles(_rootPath, "*.json");
            }
            catch (Exception ex)
            {
                // 边界失败：目录被删/无权限时返回空列表，但必须留下完整错误日志。
                _logger.LogError(ex, "枚举向量集合目录 {Directory} 失败：{Message}", _rootPath, ex.Message);
                return Task.FromResult<IReadOnlyList<CollectionInfo>>(result);
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var document = ReadDocument(file);
                    result.Add(new CollectionInfo(
                        document.Collection,
                        ToOptions(document),
                        document.Records.Count));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "读取向量集合文件 {File} 失败：{Message}", file, ex.Message);
                }
            }

            return Task.FromResult<IReadOnlyList<CollectionInfo>>(result);
        }

        public async Task DropCollectionAsync(string collection, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    var file = GetFilePath(collection);

                    if (File.Exists(file))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            throw new VectorStoreException(
                                VectorStoreErrorCodes.StorageFailure,
                                $"删除集合 '{collection}' 的文件失败：{ex.Message}", ex);
                        }

                        RemoveState(collection);
                        return;
                    }

                    if (!RemoveState(collection))
                    {
                        throw new VectorStoreException(
                            VectorStoreErrorCodes.CollectionNotFound,
                            $"集合 '{collection}' 不存在。");
                    }
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task<IReadOnlyList<VectorRecordMeta>> ListRecordsAsync(
            string collection,
            string? partition = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);

            var state = GetState(collection);
            return Task.FromResult(state.Index.ListRecords(partition));
        }

        public async Task UpsertAsync(string collection, IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            if (records is null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;

            var state = GetState(collection);
            var options = state.Options;

            var entries = new List<VectorEntry>(records.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "记录 Id 不能为空。");
                if (!seenIds.Add(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, $"批次内出现重复 Id '{record.Id}'。");
                if (record.Vector.Length != options.Dimensions)
                    throw new VectorStoreException(
                        VectorStoreErrorCodes.DimensionMismatch,
                        $"记录 '{record.Id}' 维度为 {record.Vector.Length}，集合要求 {options.Dimensions}。");

                VectorStoreGuard.ValidatePartition(record.Partition);
                entries.Add(VectorEntryFactory.Create(record, options));
            }

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    foreach (var entry in entries)
                        state.Index.AddOrReplace(entry);

                    try
                    {
                        Persist(state);
                    }
                    catch
                    {
                        // 落盘失败必须让内存索引回到磁盘状态，避免进程内检索看到未持久化的数据。
                        ReloadState(state);
                        throw;
                    }
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task DeleteAsync(string collection, VectorFilter filter, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);

            if (filter is null || IsEmptyFilter(filter))
                throw new VectorStoreException(
                    VectorStoreErrorCodes.InvalidArgument,
                    "删除必须至少指定 Partition / Tags / 时间范围之一，拒绝清空整个集合。");

            var state = GetState(collection);
            var ids = state.Index.FindIds(entry => VectorFilterMatcher.Matches(entry, filter));
            if (ids.Count == 0) return;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    state.Index.Remove(ids);

                    try
                    {
                        Persist(state);
                    }
                    catch
                    {
                        ReloadState(state);
                        throw;
                    }
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task<IReadOnlyList<VectorHit>> SearchAsync(
            string collection,
            ReadOnlyMemory<float> query,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var state = GetState(collection);
            ValidateQuery(state.Options, query, topK);
            ct.ThrowIfCancellationRequested();

            return Task.FromResult(
                state.Index.Search(query.ToArray(), topK, filter, minScore, state.Options.Metric));
        }

        public Task<IReadOnlyList<IReadOnlyList<VectorHit>>> SearchBatchAsync(
            string collection,
            IReadOnlyList<ReadOnlyMemory<float>> queries,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (queries is null) throw new ArgumentNullException(nameof(queries));
            if (queries.Count == 0)
                return Task.FromResult<IReadOnlyList<IReadOnlyList<VectorHit>>>(Array.Empty<IReadOnlyList<VectorHit>>());

            var state = GetState(collection);
            foreach (var query in queries) ValidateQuery(state.Options, query, topK);
            ct.ThrowIfCancellationRequested();

            var arrays = queries.Select(x => x.ToArray()).ToArray();
            return Task.FromResult(state.Index.SearchBatch(arrays, topK, filter, minScore, state.Options.Metric));
        }

        public Task<VectorStoreHealth> CheckHealthAsync(CancellationToken ct = default)
        {
            try
            {
                ThrowIfDisposed();
                ct.ThrowIfCancellationRequested();

                Directory.CreateDirectory(_rootPath);
                var probe = Path.Combine(_rootPath, ".health");
                File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
                File.Delete(probe);

                return Task.FromResult(new VectorStoreHealth(true, ProviderId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向量库健康检查失败：{Message}", ex.Message);
                return Task.FromResult(new VectorStoreHealth(false, ProviderId, ex.Message));
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposed) return ValueTask.CompletedTask;
                _disposed = true;
                _states.Clear();
            }

            _writeLock.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>同步释放，便于 DI 容器在同步 Dispose 路径下使用。</summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private CollectionState GetState(string collection)
        {
            VectorStoreGuard.ValidateCollectionName(collection);

            if (TryGetState(collection) is { } cached) return cached;

            var file = GetFilePath(collection);
            if (!File.Exists(file))
            {
                throw new VectorStoreException(
                    VectorStoreErrorCodes.CollectionNotFound,
                    $"集合 '{collection}' 不存在，请先调用 EnsureCollectionAsync。");
            }

            var state = LoadState(file, collection);
            return AddState(collection, state);
        }

        private CollectionState? TryGetState(string collection)
        {
            lock (_sync)
            {
                return _states.TryGetValue(collection, out var state) ? state : null;
            }
        }

        private CollectionState AddState(string collection, CollectionState state)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(collection, out var existing)) return existing;
                _states[collection] = state;
                return state;
            }
        }

        private bool RemoveState(string collection)
        {
            lock (_sync) return _states.Remove(collection);
        }

        private CollectionState LoadState(string file, string collection)
        {
            var document = ReadDocument(file);

            if (!string.Equals(document.Collection, collection, StringComparison.Ordinal))
            {
                throw new VectorStoreException(
                    VectorStoreErrorCodes.StorageFailure,
                    $"向量集合文件 {file} 记录的是 '{document.Collection}'，与请求的 '{collection}' 不一致。");
            }

            var options = ToOptions(document);
            var state = new CollectionState(collection, options, document.CreatedAtUnixMs);

            var expectedBytes = document.Dimensions * sizeof(float);
            var entries = new List<VectorEntry>(document.Records.Count);

            foreach (var record in document.Records)
            {
                var bytes = Convert.FromBase64String(record.Vector);
                if (bytes.Length != expectedBytes)
                {
                    throw new VectorStoreException(
                        VectorStoreErrorCodes.StorageFailure,
                        $"集合 '{collection}' 的记录 '{record.Id}' 向量长度 {bytes.Length} 字节，与维度 {document.Dimensions} 不符。");
                }

                var payload = record.Payload;
                entries.Add(new VectorEntry
                {
                    Id = record.Id,
                    Partition = record.Partition,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(record.CreatedAtUnixMs),
                    Vector = ToFloats(bytes),
                    Payload = payload,
                    Tags = VectorEntryFactory.ParseTags(payload)
                });
            }

            state.Index.AddRange(entries);
            return state;
        }

        private void ReloadState(CollectionState state)
        {
            var file = GetFilePath(state.Collection);

            state.Index.Clear();
            if (!File.Exists(file)) return;

            var loaded = LoadState(file, state.Collection);
            state.Index.AddRange(loaded.Index.Snapshot());
        }

        private void Persist(CollectionState state)
        {
            var document = new FileCollectionDocument
            {
                SchemaVersion = SchemaVersion,
                Collection = state.Collection,
                Dimensions = state.Options.Dimensions,
                Metric = (int)state.Options.Metric,
                Encoding = (int)state.Options.Encoding,
                EmbeddingModel = state.Options.EmbeddingModel,
                Owner = state.Options.Owner,
                CreatedAtUnixMs = state.CreatedAtUnixMs,
                Records = state.Index.Snapshot().Select(ToRecord).ToList()
            };

            var file = GetFilePath(state.Collection);
            var temp = file + ".tmp";

            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入向量集合文件 {File} 失败：{Message}", file, ex.Message);
                throw new VectorStoreException(
                    VectorStoreErrorCodes.StorageFailure,
                    $"写入集合 '{state.Collection}' 失败：{ex.Message}", ex);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private FileCollectionDocument ReadDocument(string file)
        {
            try
            {
                var json = File.ReadAllText(file);
                var document = JsonSerializer.Deserialize<FileCollectionDocument>(json, JsonOptions)
                               ?? throw new InvalidDataException("文件内容为空。");

                if (document.SchemaVersion > SchemaVersion)
                {
                    throw new InvalidDataException(
                        $"文件 schemaVersion={document.SchemaVersion} 高于支持的 {SchemaVersion}。");
                }

                if (string.IsNullOrWhiteSpace(document.Collection))
                    throw new InvalidDataException("缺少 collection 字段。");

                if (document.Dimensions <= 0)
                    throw new InvalidDataException($"dimensions={document.Dimensions} 非法。");

                if (!Enum.IsDefined((VectorMetric)document.Metric))
                    throw new InvalidDataException($"metric={document.Metric} 非法。");

                if (!Enum.IsDefined((VectorEncoding)document.Encoding))
                    throw new InvalidDataException($"encoding={document.Encoding} 非法。");

                if (document.Records is null)
                    throw new InvalidDataException("records 字段不能为 null。");

                return document;
            }
            catch (Exception ex) when (ex is not VectorStoreException)
            {
                throw new VectorStoreException(
                    VectorStoreErrorCodes.StorageFailure,
                    $"读取向量集合文件 {file} 失败：{ex.Message}", ex);
            }
        }

        private string GetFilePath(string collection)
        {
            var prefix = new StringBuilder(collection.Length);

            foreach (var ch in collection)
            {
                prefix.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_');
                if (prefix.Length >= MaxNamePrefixLength) break;
            }

            if (prefix.Length == 0) prefix.Append("collection");

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collection)))
                .ToLowerInvariant()[..16];

            return Path.Combine(_rootPath, $"{prefix}-{hash}.json");
        }

        private static CollectionOptions ToOptions(FileCollectionDocument document)
        {
            return new CollectionOptions(
                document.Dimensions,
                (VectorMetric)document.Metric,
                document.EmbeddingModel,
                document.Owner,
                (VectorEncoding)document.Encoding);
        }

        private static FileVectorRecord ToRecord(VectorEntry entry)
        {
            return new FileVectorRecord
            {
                Id = entry.Id,
                Partition = entry.Partition,
                CreatedAtUnixMs = entry.CreatedAt.ToUnixTimeMilliseconds(),
                Payload = entry.Payload is null ? null : new Dictionary<string, string>(entry.Payload),
                Vector = Convert.ToBase64String(ToBytes(entry.Vector))
            };
        }

        private static byte[] ToBytes(float[] vector)
        {
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] ToFloats(byte[] bytes)
        {
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }

        private static void ValidateQuery(CollectionOptions options, ReadOnlyMemory<float> query, int topK)
        {
            if (topK <= 0)
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "topK 必须大于 0。");
            if (query.Length != options.Dimensions)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.DimensionMismatch,
                    $"查询维度为 {query.Length}，集合要求 {options.Dimensions}。");
        }

        private static bool IsEmptyFilter(VectorFilter filter)
        {
            return filter.Partition is null &&
                   (filter.Tags is null || filter.Tags.Count == 0) &&
                   filter.From is null &&
                   filter.To is null &&
                   (filter.Ids is null || filter.Ids.Count == 0);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private sealed class CollectionState
        {
            public CollectionState(string collection, CollectionOptions options, long createdAtUnixMs)
            {
                Collection = collection;
                Options = options;
                CreatedAtUnixMs = createdAtUnixMs;
            }

            public string Collection { get; }
            public CollectionOptions Options { get; }
            public long CreatedAtUnixMs { get; }
            public VectorIndex Index { get; } = new();
        }

        private sealed class FileCollectionDocument
        {
            public int SchemaVersion { get; set; } = 1;
            public string Collection { get; set; } = string.Empty;
            public int Dimensions { get; set; }
            public int Metric { get; set; }
            public int Encoding { get; set; }
            public string? EmbeddingModel { get; set; }
            public string? Owner { get; set; }
            public long CreatedAtUnixMs { get; set; }
            public List<FileVectorRecord> Records { get; set; } = [];
        }

        private sealed class FileVectorRecord
        {
            public string Id { get; set; } = string.Empty;
            public string? Partition { get; set; }
            public long CreatedAtUnixMs { get; set; }
            public Dictionary<string, string>? Payload { get; set; }
            public string Vector { get; set; } = string.Empty;
        }
    }
}
