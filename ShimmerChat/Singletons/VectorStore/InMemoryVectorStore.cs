using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons.VectorStore
{
    /// <summary>
    /// 纯内存实现，用于测试与轻量场景；进程退出即丢失。
    /// </summary>
    public sealed class InMemoryVectorStore : IVectorStoreService, IDisposable
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
        private bool _disposed;

        public string ProviderId => "InMemory";

        public string StoreIdentity => "InMemory";

        public Task EnsureCollectionAsync(string collection, CollectionOptions options, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            VectorStoreGuard.ValidateOptions(options);
            ct.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (_states.TryGetValue(collection, out var existing))
                {
                    VectorStoreGuard.ValidateCompatible(existing.Options, options);
                    return Task.CompletedTask;
                }

                _states[collection] = new State(options);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();

            lock (_sync)
            {
                var result = _states
                    .Select(pair => new CollectionInfo(pair.Key, pair.Value.Options, pair.Value.Index.Count))
                    .ToArray();

                return Task.FromResult<IReadOnlyList<CollectionInfo>>(result);
            }
        }

        public Task DropCollectionAsync(string collection, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            ct.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!_states.Remove(collection))
                    throw new VectorStoreException(
                        VectorStoreErrorCodes.CollectionNotFound,
                        $"集合 '{collection}' 不存在。");
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorRecordMeta>> ListRecordsAsync(
            string collection,
            string? partition = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var state = GetState(collection);
            return Task.FromResult(state.Index.ListRecords(partition));
        }

        public Task UpsertAsync(string collection, IReadOnlyList<VectorRecord> records, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            if (records is null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return Task.CompletedTask;

            var state = GetState(collection);
            var entries = new List<VectorEntry>(records.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "记录 Id 不能为空。");
                if (!seenIds.Add(record.Id))
                    throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, $"批次内出现重复 Id '{record.Id}'。");
                if (record.Vector.Length != state.Options.Dimensions)
                    throw new VectorStoreException(
                        VectorStoreErrorCodes.DimensionMismatch,
                        $"记录 '{record.Id}' 维度为 {record.Vector.Length}，集合要求 {state.Options.Dimensions}。");

                VectorStoreGuard.ValidatePartition(record.Partition);
                entries.Add(VectorEntryFactory.Create(record, state.Options));
            }

            foreach (var entry in entries) state.Index.AddOrReplace(entry);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string collection, VectorFilter filter, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            VectorStoreGuard.ValidateCollectionName(collection);
            if (filter is null || IsEmptyFilter(filter))
                throw new VectorStoreException(
                    VectorStoreErrorCodes.InvalidArgument,
                    "删除必须至少指定 Partition / Tags / 时间范围之一，拒绝清空整个集合。");

            var state = GetState(collection);
            var ids = state.Index.FindIds(entry => VectorFilterMatcher.Matches(entry, filter));
            if (ids.Count > 0) state.Index.Remove(ids);
            return Task.CompletedTask;
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

            var arrays = queries.Select(x => x.ToArray()).ToArray();
            return Task.FromResult(state.Index.SearchBatch(arrays, topK, filter, minScore, state.Options.Metric));
        }

        public Task<VectorStoreHealth> CheckHealthAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return Task.FromResult(new VectorStoreHealth(true, ProviderId));
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                _states.Clear();
                _disposed = true;
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>同步释放，便于 DI 容器在同步 Dispose 路径下使用。</summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private State GetState(string collection)
        {
            VectorStoreGuard.ValidateCollectionName(collection);

            lock (_sync)
            {
                if (_states.TryGetValue(collection, out var state)) return state;
            }

            throw new VectorStoreException(
                VectorStoreErrorCodes.CollectionNotFound,
                $"集合 '{collection}' 不存在，请先调用 EnsureCollectionAsync。");
        }

        private void ValidateQuery(CollectionOptions options, ReadOnlyMemory<float> query, int topK)
        {
            if (topK <= 0)
                throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "topK 必须大于 0。");
            if (query.Length != options.Dimensions)
                throw new VectorStoreException(
                    VectorStoreErrorCodes.DimensionMismatch,
                    $"查询维度为 {query.Length}，集合要求 {options.Dimensions}。");
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static bool IsEmptyFilter(VectorFilter filter)
        {
            return filter.Partition is null &&
                   (filter.Tags is null || filter.Tags.Count == 0) &&
                   filter.From is null &&
                   filter.To is null &&
                   (filter.Ids is null || filter.Ids.Count == 0);
        }

        private sealed class State
        {
            public State(CollectionOptions options) => Options = options;

            public CollectionOptions Options { get; }
            public VectorIndex Index { get; } = new();
        }
    }
}
