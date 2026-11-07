using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiteDB;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons.VectorStore
{
    /// <summary>
    /// LiteDB 持久化 + 进程内扁平索引的向量存储实现。
    /// LiteDB 只承担 BLOB 持久化，检索全部在内存索引中完成。
    /// </summary>
    public sealed class LiteDbVectorStore : IVectorStoreService, IDisposable
    {
        private const string MetaCollectionName = "_meta";
        private const int SchemaVersion = 1;

        private readonly VectorStoreConfig _config;
        private readonly ILogger<LiteDbVectorStore> _logger;
        private readonly string _rootPath;
        private readonly bool _perCollection;

        private readonly object _sync = new();
        private readonly Dictionary<string, LiteDatabase> _databases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CollectionState> _states = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _disposed;

        public LiteDbVectorStore(VectorStoreConfig config, ILogger<LiteDbVectorStore> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _rootPath = Path.IsPathRooted(config.RootPath)
                ? config.RootPath
                : Path.Combine(AppContext.BaseDirectory, config.RootPath.
                    TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _perCollection = config.IsPerCollectionScope();
            Directory.CreateDirectory(_rootPath);
        }

        public string ProviderId => "LiteDb";

        public string StoreIdentity => $"LiteDb:{_rootPath}";

        public async Task EnsureCollectionAsync(string collection, CollectionOptions options, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            VectorStoreGuard.ValidateOptions(options);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() => EnsureCore(collection, options), ct).ConfigureAwait(false);
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

            if (!Directory.Exists(_rootPath))
                return Task.FromResult<IReadOnlyList<CollectionInfo>>(result);

            foreach (var file in Directory.GetFiles(_rootPath, "*.db"))
            {
                // LiteDB 的预写日志文件与数据文件同目录，跳过。
                if (file.EndsWith("-log.db", StringComparison.OrdinalIgnoreCase)) continue;

                ct.ThrowIfCancellationRequested();
                try
                {
                    var database = GetDatabase(file);
                    var metaCollection = database.GetCollection<CollectionMetaDoc>(MetaCollectionName);

                    foreach (var meta in metaCollection.FindAll())
                    {
                        var count = database.GetCollection<VectorDoc>(meta.Physical).Count();
                        result.Add(new CollectionInfo(meta.Id, ToOptions(meta), count));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "读取向量库文件 {File} 失败：{Message}", file, ex.Message);
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
                var state = GetState(collection);

                await Task.Run(() =>
                {
                    try
                    {
                        state.Db.DropCollection(state.Meta.Physical);
                        state.Db.GetCollection<CollectionMetaDoc>(MetaCollectionName).Delete(collection);
                    }
                    catch (LiteException ex)
                    {
                        throw new VectorStoreException(
                            VectorStoreErrorCodes.StorageFailure,
                            $"删除集合 '{collection}' 失败：{ex.Message}", ex);
                    }
                }, ct).ConfigureAwait(false);

                lock (_sync)
                {
                    _states.Remove(collection);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<IReadOnlyList<VectorRecordMeta>> ListRecordsAsync(
            string collection,
            string? partition = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            var state = GetState(collection);

            if (partition is { } key)
                await EnsurePartitionLoadedAsync(state, key, ct).ConfigureAwait(false);
            else
                await EnsureLoadedAllAsync(state, ct).ConfigureAwait(false);

            return state.Index.ListRecords(partition);
        }

        public async Task UpsertAsync(string collection, IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            if (records is null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;

            var state = GetState(collection);
            var options = ToOptions(state.Meta);

            var entries = new List<VectorEntry>(records.Count);
            var documents = new List<VectorDoc>(records.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "记录 Id 不能为空。");
                if (!seenIds.Add(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, $"批次内出现重复 Id '{record.Id}'。");
                if (record.Vector.Length != state.Meta.Dimensions)
                    throw new VectorStoreException(
                        VectorStoreErrorCodes.DimensionMismatch,
                        $"记录 '{record.Id}' 维度为 {record.Vector.Length}，集合要求 {state.Meta.Dimensions}。");

                VectorStoreGuard.ValidatePartition(record.Partition);

                var entry = VectorEntryFactory.Create(record, options);
                entries.Add(entry);
                documents.Add(ToDocument(entry));
            }

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var collectionHandle = state.Db.GetCollection<VectorDoc>(state.Meta.Physical);
                        state.Db.BeginTrans();
                        try
                        {
                            collectionHandle.Upsert(documents);
                            state.Db.Commit();
                        }
                        catch
                        {
                            state.Db.Rollback();
                            throw;
                        }
                    }
                    catch (LiteException ex)
                    {
                        throw new VectorStoreException(
                            VectorStoreErrorCodes.StorageFailure,
                            $"写入集合 '{collection}' 失败：{ex.Message}", ex);
                    }
                }, ct).ConfigureAwait(false);

                UpdateIndexAfterWrite(state, entries);
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

            if (filter.Partition is { } partition)
                await EnsurePartitionLoadedAsync(state, partition, ct).ConfigureAwait(false);
            else
                await EnsureLoadedAllAsync(state, ct).ConfigureAwait(false);

            var ids = state.Index.FindIds(entry => VectorFilterMatcher.Matches(entry, filter));
            if (ids.Count == 0) return;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var collectionHandle = state.Db.GetCollection<VectorDoc>(state.Meta.Physical);
                        state.Db.BeginTrans();
                        try
                        {
                            foreach (var id in ids) collectionHandle.Delete(id);
                            state.Db.Commit();
                        }
                        catch
                        {
                            state.Db.Rollback();
                            throw;
                        }
                    }
                    catch (LiteException ex)
                    {
                        throw new VectorStoreException(
                            VectorStoreErrorCodes.StorageFailure,
                            $"删除集合 '{collection}' 的记录失败：{ex.Message}", ex);
                    }
                }, ct).ConfigureAwait(false);

                state.Index.Remove(ids);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<IReadOnlyList<VectorHit>> SearchAsync(
            string collection,
            ReadOnlyMemory<float> query,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            var state = GetState(collection);
            ValidateQuery(state.Meta, query, topK);

            if (filter?.Partition is { } partition)
                await EnsurePartitionLoadedAsync(state, partition, ct).ConfigureAwait(false);
            else
                await EnsureLoadedAllAsync(state, ct).ConfigureAwait(false);

            return state.Index.Search(query.ToArray(), topK, filter, minScore, (VectorMetric)state.Meta.Metric);
        }

        public async Task<IReadOnlyList<IReadOnlyList<VectorHit>>> SearchBatchAsync(
            string collection,
            IReadOnlyList<ReadOnlyMemory<float>> queries,
            int topK,
            VectorFilter? filter = null,
            float minScore = 0f,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            if (queries is null) throw new ArgumentNullException(nameof(queries));
            if (queries.Count == 0) return [];

            var state = GetState(collection);
            foreach (var query in queries) ValidateQuery(state.Meta, query, topK);

            if (filter?.Partition is { } partition)
                await EnsurePartitionLoadedAsync(state, partition, ct).ConfigureAwait(false);
            else
                await EnsureLoadedAllAsync(state, ct).ConfigureAwait(false);

            var arrays = queries.Select(x => x.ToArray()).ToArray();
            return state.Index.SearchBatch(arrays, topK, filter, minScore, (VectorMetric)state.Meta.Metric);
        }

        public Task<VectorStoreHealth> CheckHealthAsync(CancellationToken ct = default)
        {
            try
            {
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
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            lock (_sync)
            {
                foreach (var database in _databases.Values) database.Dispose();
                _databases.Clear();
                _states.Clear();
            }

            _writeLock.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>同步释放，便于 DI 容器在同步 Dispose 路径下使用。</summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private void EnsureCore(string collection, CollectionOptions options)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(collection, out var existing))
                {
                    ValidateMeta(existing.Meta, options);
                    return;
                }
            }

            var owner = ResolveOwner(collection);
            var path = GetDatabasePath(owner);
            var database = GetDatabase(path);
            var metaCollection = database.GetCollection<CollectionMetaDoc>(MetaCollectionName);
            var meta = metaCollection.FindById(collection);

            if (meta is null)
            {
                meta = new CollectionMetaDoc
                {
                    Id = collection,
                    Physical = "v_" + Hash(collection)[..16],
                    Dimensions = options.Dimensions,
                    Metric = (int)options.Metric,
                    Encoding = (int)options.Encoding,
                    EmbeddingModel = options.EmbeddingModel,
                    Owner = owner,
                    SchemaVersion = SchemaVersion,
                    CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                metaCollection.Upsert(meta);
                database.GetCollection<VectorDoc>(meta.Physical).EnsureIndex(x => x.Partition);
            }
            else
            {
                ValidateMeta(meta, options);
            }

            lock (_sync)
            {
                _states[collection] = new CollectionState { Meta = meta, Db = database };
            }
        }

        private async Task EnsureLoadedAllAsync(CollectionState state, CancellationToken ct)
        {
            if (state.FullyLoaded) return;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (state.FullyLoaded) return;

                var documents = await Task.Run(
                    () => state.Db.GetCollection<VectorDoc>(state.Meta.Physical).FindAll().ToList(),
                    ct).ConfigureAwait(false);

                var index = new VectorIndex();
                index.AddRange(documents.Select(ToEntry));

                state.Index = index;
                state.LoadedPartitions.Clear();
                state.FullyLoaded = true;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task EnsurePartitionLoadedAsync(CollectionState state, string partition, CancellationToken ct)
        {
            if (state.FullyLoaded || state.LoadedPartitions.Contains(partition)) return;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (state.FullyLoaded || state.LoadedPartitions.Contains(partition)) return;

                var documents = await Task.Run(
                    () => state.Db.GetCollection<VectorDoc>(state.Meta.Physical)
                        .Find(x => x.Partition == partition)
                        .ToList(),
                    ct).ConfigureAwait(false);

                state.Index.AddRange(documents.Select(ToEntry));
                state.LoadedPartitions.Add(partition);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private void UpdateIndexAfterWrite(CollectionState state, IReadOnlyList<VectorEntry> entries)
        {
            List<VectorEntry>? toIndex = null;

            lock (_sync)
            {
                if (state.FullyLoaded)
                {
                    toIndex = entries.ToList();
                }
                else
                {
                    foreach (var entry in entries)
                    {
                        if (!state.LoadedPartitions.Contains(entry.Partition ?? string.Empty)) continue;
                        (toIndex ??= []).Add(entry);
                    }
                }
            }

            if (toIndex is null) return;
            foreach (var entry in toIndex) state.Index.AddOrReplace(entry);
        }

        private void ValidateQuery(CollectionMetaDoc meta, ReadOnlyMemory<float> query, int topK)
        {
            if (topK <= 0)
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "topK 必须大于 0。");
            if (query.Length != meta.Dimensions)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.DimensionMismatch,
                    $"查询维度为 {query.Length}，集合要求 {meta.Dimensions}。");
        }

        private static void ValidateMeta(CollectionMetaDoc meta, CollectionOptions options)
        {
            VectorStoreGuard.ValidateCompatible(ToOptions(meta), options);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private CollectionState GetState(string collection)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(collection, out var state)) return state;

                var loaded = TryLoadState(collection);
                if (loaded is not null)
                {
                    _states[collection] = loaded;
                    return loaded;
                }
            }

            throw new VectorStoreException(
                VectorStoreErrorCodes.CollectionNotFound,
                $"集合 '{collection}' 不存在，请先调用 EnsureCollectionAsync。");
        }

        /// <summary>
        /// 进程重启后首次访问时，从已有文件里恢复集合元数据。
        /// </summary>
        private CollectionState? TryLoadState(string collection)
        {
            var path = GetDatabasePath(ResolveOwner(collection));
            if (!File.Exists(path)) return null;

            var database = GetDatabase(path);
            var meta = database.GetCollection<CollectionMetaDoc>(MetaCollectionName).FindById(collection);
            if (meta is null) return null;

            return new CollectionState { Meta = meta, Db = database };
        }

        private LiteDatabase GetDatabase(string path)
        {
            lock (_sync)
            {
                if (_databases.TryGetValue(path, out var database)) return database;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                database = new LiteDatabase($"Filename={path};Connection=direct");
                _databases[path] = database;
                return database;
            }
        }

        private string ResolveOwner(string collection)
        {
            if (_perCollection) return collection;

            if (collection.StartsWith("plugin:", StringComparison.Ordinal))
            {
                var rest = collection["plugin:".Length..];
                var separator = rest.IndexOf(':');
                var pluginId = separator >= 0 ? rest[..separator] : rest;
                if (pluginId.Length > 0) return "plugin:" + pluginId;
            }

            return "default";
        }

        private string GetDatabasePath(string owner)
        {
            if (owner == "default") return Path.Combine(_rootPath, "default.db");

            var safe = SafeFileName(owner);
            var hash = Hash(owner)[..8];
            return Path.Combine(_rootPath, $"{safe}-{hash}.db");
        }

        private static string SafeFileName(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_');
                if (builder.Length >= 40) break;
            }

            return builder.Length == 0 ? "store" : builder.ToString();
        }

        private static string Hash(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static bool IsEmptyFilter(VectorFilter filter)
        {
            return filter.Partition is null &&
                   (filter.Tags is null || filter.Tags.Count == 0) &&
                   filter.From is null &&
                   filter.To is null &&
                   (filter.Ids is null || filter.Ids.Count == 0);
        }

        private static CollectionOptions ToOptions(CollectionMetaDoc meta)
        {
            return new CollectionOptions(
                meta.Dimensions,
                (VectorMetric)meta.Metric,
                meta.EmbeddingModel,
                meta.Owner,
                (VectorEncoding)meta.Encoding);
        }

        private static VectorDoc ToDocument(VectorEntry entry)
        {
            return new VectorDoc
            {
                Id = entry.Id,
                Partition = entry.Partition,
                CreatedAtUnixMs = entry.CreatedAt.ToUnixTimeMilliseconds(),
                PayloadJson = entry.Payload is null ? null : System.Text.Json.JsonSerializer.Serialize(entry.Payload),
                Vector = ToBytes(entry.Vector)
            };
        }

        private static VectorEntry ToEntry(VectorDoc document)
        {
            IReadOnlyDictionary<string, string>? payload = null;

            if (!string.IsNullOrEmpty(document.PayloadJson))
            {
                payload = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(document.PayloadJson);
            }

            return new VectorEntry
            {
                Id = document.Id,
                Partition = document.Partition,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(document.CreatedAtUnixMs),
                Vector = ToFloats(document.Vector),
                Payload = payload,
                Tags = VectorEntryFactory.ParseTags(payload)
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
            if (bytes.Length % sizeof(float) != 0)
                throw new VectorStoreException(VectorStoreErrorCodes.StorageFailure, "向量 BLOB 长度不是 4 的倍数。");

            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }

        private sealed class CollectionState
        {
            public required CollectionMetaDoc Meta { get; init; }
            public required LiteDatabase Db { get; init; }
            public VectorIndex Index { get; set; } = new();
            public HashSet<string> LoadedPartitions { get; } = new(StringComparer.Ordinal);
            public bool FullyLoaded { get; set; }
        }

        private sealed class CollectionMetaDoc
        {
            public string Id { get; set; } = string.Empty;
            public string Physical { get; set; } = string.Empty;
            public int Dimensions { get; set; }
            public int Metric { get; set; }
            public int Encoding { get; set; }
            public string? EmbeddingModel { get; set; }
            public string? Owner { get; set; }
            public int SchemaVersion { get; set; }
            public long CreatedAtUnixMs { get; set; }
        }

        private sealed class VectorDoc
        {
            public string Id { get; set; } = string.Empty;
            public string? Partition { get; set; }
            public long CreatedAtUnixMs { get; set; }
            public string? PayloadJson { get; set; }
            public byte[] Vector { get; set; } = [];
        }
    }
}
