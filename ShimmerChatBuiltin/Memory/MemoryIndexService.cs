using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Memory
{
    public enum MemoryIndexStatus
    {
        Missing,
        Ready,
        Suspect,
        Partial,
        Error
    }

    public sealed record MemoryHit(
        string Id,
        float Score,
        string Content,
        string Tags,
        DateTimeOffset UpdatedAt);

    public sealed record MemorySyncProgress(int Done, int Total, int Failed, string Phase);

    public sealed record MemorySyncReport(
        int Missing,
        int Stale,
        int Orphans,
        int Records,
        long Revision,
        string? Error = null);

    public sealed class MemorySyncPlan
    {
        public List<MemoryRecord> Missing { get; } = [];
        public List<MemoryRecord> Stale { get; } = [];
        public List<string> OrphanIds { get; } = [];
        public long Revision { get; init; }
        public int Total => Missing.Count + Stale.Count;
    }

    /// <summary>当前模型索引未就绪，检索必须明确失败而不是返回空结果。</summary>
    public sealed class MemoryNotReadyException : Exception
    {
        public MemoryNotReadyException(MemoryIndexStatus status, string message, Exception? inner = null)
            : base(message, inner)
        {
            Status = status;
        }

        public MemoryIndexStatus Status { get; }
    }

    /// <summary>文本已保存但向量写入失败；记录本身是安全的。</summary>
    public sealed class MemoryVectorSyncException : Exception
    {
        public MemoryVectorSyncException(string recordId, string message, Exception? inner = null)
            : base(message, inner)
        {
            RecordId = recordId;
        }

        public string RecordId { get; }
    }

    /// <summary>
    /// 记忆领域服务：真值记录 + 当前模型索引。所有写入都会尽量写穿到当前模型集合，
    /// 切换模型时通过 diff 补差，而不是整库重建。
    /// </summary>
    public sealed class MemoryIndexService
    {
        private const int EmbedBatchSize = 16;
        private const string VersionPayloadKey = "version";
        private const string TagsPayloadKey = "tags";

        private readonly MemoryRecordStore _store;
        private readonly IVectorStoreService _vectors;
        private readonly MemoryEmbeddingClient _embeddings;
        private readonly ILogger _logger;

        public MemoryIndexService(
            MemoryRecordStore store,
            IVectorStoreService vectors,
            MemoryModelConfig config,
            ILogger? logger = null)
        {
            _store = store;
            _vectors = vectors;
            Config = config;
            _embeddings = new MemoryEmbeddingClient(config);
            _logger = logger ?? NullLogger.Instance;
        }

        public MemoryModelConfig Config { get; }

        public string CollectionName => Config.CollectionName;

        public MemoryModelState? State => _store.GetState(Config.ModelKey);

        public MemoryIndexStatus Snapshot()
        {
            var state = _store.GetState(Config.ModelKey);
            if (state is null) return MemoryIndexStatus.Missing;

            var status = state.Status switch
            {
                MemoryStateStatus.Error => MemoryIndexStatus.Error,
                MemoryStateStatus.Partial => MemoryIndexStatus.Partial,
                _ => state.BuiltRevision == _store.Revision && state.Count == _store.TotalCount()
                    ? MemoryIndexStatus.Ready
                    : MemoryIndexStatus.Suspect
            };

            // 旧状态没有 StoreIdentity（空）时跳过校验，避免把旧数据误判为不可用。
            if (!string.IsNullOrEmpty(state.StoreIdentity) &&
                !string.Equals(state.StoreIdentity, _vectors.StoreIdentity, StringComparison.Ordinal))
            {
                return MemoryIndexStatus.Suspect;
            }

            return status;
        }

        /// <summary>
        /// 结合向量库实际内容判断状态。用于面板展示与检索前的就绪校验：
        /// 状态记录可能仍在，但向量集合已被删除、切换了后端目录，或只同步过某个 Agent。
        /// </summary>
        public async Task<MemoryIndexStatus> GetStatusAsync(
            string? agentId,
            CancellationToken ct = default)
        {
            var status = Snapshot();
            if (status is MemoryIndexStatus.Missing or MemoryIndexStatus.Error)
                return status;

            var collections = await _vectors.ListCollectionsAsync(ct).ConfigureAwait(false);
            var collection = collections.FirstOrDefault(x =>
                string.Equals(x.Name, CollectionName, StringComparison.Ordinal));

            if (collection is null)
            {
                MarkSuspect();
                return MemoryIndexStatus.Suspect;
            }

            if (agentId is null)
            {
                if (collection.Count != _store.TotalCount())
                {
                    MarkSuspect();
                    return MemoryIndexStatus.Suspect;
                }

                return status;
            }

            if (status == MemoryIndexStatus.Ready) return status;

            // 全局状态可能是 Partial/Suspect（其它 Agent 未同步或曾写入失败）；
            // 只要该 Agent 自己的分区无缺失/过期/孤儿，就可以正常检索。
            var plan = await PlanAsync(agentId, ct).ConfigureAwait(false);
            return plan.Total == 0 && plan.OrphanIds.Count == 0
                ? MemoryIndexStatus.Ready
                : status;
        }

        public async Task<MemorySyncPlan> PlanAsync(string? agentId, CancellationToken ct = default)
        {
            var all = _store.AllForIndex();
            var records = agentId is null ? all : all.Where(x => x.AgentId == agentId).ToList();

            IReadOnlyList<VectorRecordMeta> vectors;
            try
            {
                vectors = await _vectors.ListRecordsAsync(CollectionName, agentId, ct).ConfigureAwait(false);
            }
            catch (VectorStoreException ex) when (ex.Code == VectorStoreErrorCodes.CollectionNotFound)
            {
                vectors = [];
            }

            var plan = new MemorySyncPlan { Revision = _store.Revision };
            var byId = vectors.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var recordIds = new HashSet<string>(records.Select(x => x.Id), StringComparer.Ordinal);

            foreach (var record in records)
            {
                if (!byId.TryGetValue(record.Id, out var meta))
                {
                    plan.Missing.Add(record);
                }
                else if (GetVersion(meta) != record.Version)
                {
                    plan.Stale.Add(record);
                }
            }

            foreach (var meta in vectors)
            {
                if (!recordIds.Contains(meta.Id)) plan.OrphanIds.Add(meta.Id);
            }

            return plan;
        }

        public async Task<MemorySyncReport> SyncAsync(
            MemorySyncPlan plan,
            IProgress<MemorySyncProgress>? progress = null,
            CancellationToken ct = default)
        {
            var pending = plan.Missing.Concat(plan.Stale).ToList();

            var dimensions = State?.Dimensions ?? 0;
            if (dimensions <= 0)
            {
                dimensions = pending.Count > 0
                    ? (await _embeddings.EmbedAsync([pending[0].Content], ct).ConfigureAwait(false))[0].Length
                    : await _embeddings.ProbeDimensionsAsync(ct).ConfigureAwait(false);
            }

            await _vectors.EnsureCollectionAsync(
                CollectionName,
                new CollectionOptions(dimensions, VectorMetric.Cosine, Config.ModelKey),
                ct).ConfigureAwait(false);

            var done = 0;
            var failed = 0;
            string? lastError = null;
            progress?.Report(new MemorySyncProgress(0, pending.Count, 0, "embedding"));

            for (var offset = 0; offset < pending.Count; offset += EmbedBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = pending.Skip(offset).Take(EmbedBatchSize).ToList();

                try
                {
                    var vectors = await _embeddings
                        .EmbedAsync(batch.Select(x => x.Content).ToList(), ct)
                        .ConfigureAwait(false);

                    var records = batch
                        .Select((record, index) => ToVectorRecord(record, vectors[index]))
                        .ToList();

                    await _vectors.UpsertAsync(CollectionName, records, ct).ConfigureAwait(false);
                    done += batch.Count;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed += batch.Count;
                    lastError = ex.Message;
                    _logger.LogError(
                        ex,
                        "记忆索引构建失败（模型 {Model}，记录 {RecordIds}）：{Message}",
                        Config.ModelKey,
                        string.Join(",", batch.Select(x => x.Id)),
                        ex.Message);
                }

                progress?.Report(new MemorySyncProgress(done, pending.Count, failed, "embedding"));
            }

            if (plan.OrphanIds.Count > 0)
            {
                try
                {
                    await _vectors.DeleteAsync(
                        CollectionName,
                        new VectorFilter(Ids: plan.OrphanIds.ToList()),
                        ct).ConfigureAwait(false);
                }
                catch (VectorStoreException ex)
                {
                    failed++;
                    lastError = ex.Message;
                    _logger.LogError(
                        ex,
                        "清理记忆孤儿向量失败（集合 {Collection}，孤儿 {OrphanIds}）：{Message}",
                        CollectionName,
                        string.Join(",", plan.OrphanIds),
                        ex.Message);
                }
            }

            var state = _store.GetState(Config.ModelKey) ?? new MemoryModelState
            {
                Id = Config.ModelKey,
                ModelName = Config.ModelName,
                BaseUrl = Config.BaseUrl
            };

            // 用一次全局对账决定是否真正 Ready：按 Agent 增量同步时不能直接宣称整库就绪。
            var remaining = await PlanAsync(null, ct).ConfigureAwait(false);
            var complete = remaining.Total == 0 && remaining.OrphanIds.Count == 0;

            state.Dimensions = dimensions;
            state.ModelName = Config.ModelName;
            state.BaseUrl = Config.BaseUrl;
            state.StoreIdentity = _vectors.StoreIdentity;
            state.BuiltRevision = complete ? _store.Revision : state.BuiltRevision;
            state.Count = _store.TotalCount();
            state.BuiltAt = DateTimeOffset.UtcNow;
            state.Status = complete ? MemoryStateStatus.Ready : MemoryStateStatus.Partial;
            _store.SaveState(state);

            progress?.Report(new MemorySyncProgress(done, pending.Count, failed, "done"));

            return new MemorySyncReport(
                remaining.Missing.Count,
                remaining.Stale.Count,
                remaining.OrphanIds.Count,
                state.Count,
                _store.Revision,
                lastError);
        }

        public async Task<MemorySyncReport> VerifyAsync(string? agentId, CancellationToken ct = default)
        {
            var plan = await PlanAsync(agentId, ct).ConfigureAwait(false);
            var state = _store.GetState(Config.ModelKey);

            if (plan.Total == 0 && plan.OrphanIds.Count == 0)
            {
                if (state is not null)
                {
                    state.BuiltRevision = _store.Revision;
                    state.Count = _store.TotalCount();
                    state.StoreIdentity = _vectors.StoreIdentity;
                    state.Status = MemoryStateStatus.Ready;
                    state.BuiltAt = DateTimeOffset.UtcNow;
                    _store.SaveState(state);
                }
            }
            else if (state is not null)
            {
                state.Status = MemoryStateStatus.Suspect;
                _store.SaveState(state);
            }

            return new MemorySyncReport(
                plan.Missing.Count,
                plan.Stale.Count,
                plan.OrphanIds.Count,
                _store.TotalCount(),
                _store.Revision);
        }

        public async Task<MemoryRecord> AddAsync(
            string agentId,
            string content,
            string tags = "",
            string? source = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("记忆内容不能为空。", nameof(content));

            var embedded = await _embeddings.EmbedAsync([content], ct).ConfigureAwait(false);
            await EnsureCollectionAsync(embedded[0].Length, ct).ConfigureAwait(false);

            var before = CaptureSyncState();
            var record = _store.Add(agentId, content, tags, source);

            try
            {
                await _vectors.UpsertAsync(CollectionName, [ToVectorRecord(record, embedded[0])], ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkSuspect();
                _logger.LogError(ex, "记忆 {RecordId} 已保存，但向量写入失败：{Message}", record.Id, ex.Message);
                throw new MemoryVectorSyncException(record.Id, $"记忆已保存，但向量写入失败：{ex.Message}", ex);
            }

            TouchAfterWrite(before, before.Revision + 1, before.Count + 1);
            return record;
        }

        public async Task<MemoryRecord> UpdateAsync(
            string id,
            string? content,
            string? tags,
            CancellationToken ct = default)
        {
            if (content is not null && string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("记忆内容不能为空。", nameof(content));

            var existing = _store.Get(id) ?? throw new KeyNotFoundException($"记忆 '{id}' 不存在。");
            var before = CaptureSyncState();

            var updated = _store.Update(id, content, tags);
            var contentChanged = !string.Equals(existing.Content, updated.Content, StringComparison.Ordinal);

            if (contentChanged)
            {
                try
                {
                    var embedded = await _embeddings.EmbedAsync([updated.Content], ct).ConfigureAwait(false);
                    await _vectors.UpsertAsync(CollectionName, [ToVectorRecord(updated, embedded[0])], ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    MarkSuspect();
                    _logger.LogError(ex, "记忆 {RecordId} 文本已保存，但向量更新失败：{Message}", updated.Id, ex.Message);
                    throw new MemoryVectorSyncException(updated.Id, $"文本已保存，但向量更新失败：{ex.Message}", ex);
                }
            }
            else if (tags is not null)
            {
                await UpdateTagsPayloadAsync(updated, ct).ConfigureAwait(false);
            }

            TouchAfterWrite(before, before.Revision + 1, before.Count);
            return updated;
        }

        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            var record = _store.Get(id);
            if (record is null) return;

            var before = CaptureSyncState();
            if (!_store.Delete(id)) return;

            try
            {
                await _vectors.DeleteAsync(
                    CollectionName,
                    new VectorFilter(Partition: record.AgentId, Ids: [id]),
                    ct).ConfigureAwait(false);
            }
            catch (VectorStoreException ex)
            {
                // 向量删不掉只是留下孤儿，索引对账时会清理。
                MarkSuspect();
                _logger.LogWarning(
                    ex,
                    "记忆 {RecordId} 已删除，但向量删除失败（将作为孤儿清理）：{Message}",
                    id,
                    ex.Message);
            }

            TouchAfterWrite(before, before.Revision + 1, before.Count - 1);
        }

        public async Task<IReadOnlyList<MemoryHit>> SearchAsync(
            string agentId,
            IReadOnlyList<string> queries,
            int topK,
            float minScore,
            CancellationToken ct = default)
        {
            var status = Snapshot();
            if (status != MemoryIndexStatus.Ready)
                status = await GetStatusAsync(agentId, ct).ConfigureAwait(false);

            if (status != MemoryIndexStatus.Ready)
                throw new MemoryNotReadyException(status, $"记忆索引状态为 {status}，请先构建或校验。");

            if (queries.Count == 0) return [];

            var embedded = await _embeddings.EmbedAsync(queries, ct).ConfigureAwait(false);
            var memories = embedded.Select(x => (ReadOnlyMemory<float>)x).ToArray();

            IReadOnlyList<IReadOnlyList<VectorHit>> results;
            try
            {
                results = await _vectors.SearchBatchAsync(
                    CollectionName,
                    memories,
                    topK,
                    new VectorFilter(Partition: agentId),
                    minScore,
                    ct).ConfigureAwait(false);
            }
            catch (VectorStoreException ex) when (ex.Code is
                VectorStoreErrorCodes.CollectionNotFound or VectorStoreErrorCodes.DimensionMismatch)
            {
                MarkSuspect();
                _logger.LogError(ex, "记忆向量集合不可用（集合 {Collection}）：{Message}", CollectionName, ex.Message);
                throw new MemoryNotReadyException(MemoryIndexStatus.Suspect, $"记忆向量集合不可用：{ex.Message}", ex);
            }

            var best = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var hits in results)
            {
                foreach (var hit in hits)
                {
                    if (!best.TryGetValue(hit.Id, out var score) || hit.Score > score)
                        best[hit.Id] = hit.Score;
                }
            }

            var merged = new List<MemoryHit>(best.Count);
            foreach (var pair in best)
            {
                var record = _store.Get(pair.Key);
                if (record is null) continue;
                merged.Add(new MemoryHit(record.Id, pair.Value, record.Content, record.Tags, record.UpdatedAt));
            }

            return merged.OrderByDescending(x => x.Score).ToList();
        }

        public IReadOnlyList<MemoryHit> SearchKeyword(string agentId, string keyword, int limit = 20)
        {
            return _store.List(agentId, 0, limit, keyword)
                .Select(x => new MemoryHit(x.Id, 0f, x.Content, x.Tags, x.UpdatedAt))
                .ToList();
        }

        private async Task EnsureCollectionAsync(int dimensions, CancellationToken ct)
        {
            var state = _store.GetState(Config.ModelKey);
            if (state is not null && state.Dimensions > 0 && state.Dimensions != dimensions)
                throw new InvalidOperationException(
                    $"模型维度从 {state.Dimensions} 变为 {dimensions}，请使用新的模型配置或重建索引。");

            await _vectors.EnsureCollectionAsync(
                CollectionName,
                new CollectionOptions(dimensions, VectorMetric.Cosine, Config.ModelKey),
                ct).ConfigureAwait(false);

            if (state is null)
            {
                _store.SaveState(new MemoryModelState
                {
                    Id = Config.ModelKey,
                    ModelName = Config.ModelName,
                    BaseUrl = Config.BaseUrl,
                    StoreIdentity = _vectors.StoreIdentity,
                    Dimensions = dimensions,
                    Status = MemoryStateStatus.Missing
                });
            }
            else
            {
                state.StoreIdentity = _vectors.StoreIdentity;
                if (state.Dimensions != dimensions) state.Dimensions = dimensions;
                _store.SaveState(state);
            }
        }

        private async Task UpdateTagsPayloadAsync(MemoryRecord record, CancellationToken ct)
        {
            // 标签存在向量 payload 中；当前契约没有独立的 payload 更新能力，暂用重嵌入保证一致。
            try
            {
                var embedded = await _embeddings.EmbedAsync([record.Content], ct).ConfigureAwait(false);
                await _vectors.UpsertAsync(CollectionName, [ToVectorRecord(record, embedded[0])], ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkSuspect();
                _logger.LogError(ex, "记忆 {RecordId} 标签已保存，但向量 payload 更新失败：{Message}", record.Id, ex.Message);
            }
        }

        private readonly record struct SyncSnapshot(bool InSync, long Revision, int Count);

        /// <summary>
        /// 记录写入前的一致性快照。后续用 revision/count 的期望值判断期间是否有并发写入，
        /// 避免并发写时把「另一个写入尚未写穿」的状态误标为 Ready。
        /// </summary>
        private SyncSnapshot CaptureSyncState()
        {
            var state = _store.GetState(Config.ModelKey);
            var count = _store.TotalCount();
            var revision = _store.Revision;
            var inSync = state is null || (state.Count == count && state.BuiltRevision == revision);
            return new SyncSnapshot(inSync, revision, count);
        }

        private void TouchAfterWrite(SyncSnapshot before, long expectedRevision, int expectedCount)
        {
            var state = _store.GetState(Config.ModelKey);
            if (state is null) return;

            var revision = _store.Revision;
            var count = _store.TotalCount();
            var noConcurrentWrite = revision == expectedRevision && count == expectedCount;

            // 写穿成功、写入前一致且期间没有并发写入，才能继续宣称 Ready。
            if (before.InSync && noConcurrentWrite &&
                state.Status is MemoryStateStatus.Ready or MemoryStateStatus.Missing)
            {
                state.BuiltRevision = revision;
                state.Count = count;
                state.StoreIdentity = _vectors.StoreIdentity;
                state.Status = MemoryStateStatus.Ready;
                state.BuiltAt = DateTimeOffset.UtcNow;
                _store.SaveState(state);
                return;
            }

            if (state.Status == MemoryStateStatus.Ready)
            {
                state.Status = MemoryStateStatus.Suspect;
                _store.SaveState(state);
            }
        }

        private void MarkSuspect()
        {
            var state = _store.GetState(Config.ModelKey);
            if (state is null) return;
            state.Status = MemoryStateStatus.Suspect;
            _store.SaveState(state);
        }

        private VectorRecord ToVectorRecord(MemoryRecord record, float[] vector)
        {
            return new VectorRecord(
                record.Id,
                vector,
                record.AgentId,
                record.UpdatedAt,
                new Dictionary<string, string>
                {
                    [VersionPayloadKey] = record.Version.ToString(),
                    [TagsPayloadKey] = record.Tags
                });
        }

        private static long GetVersion(VectorRecordMeta meta)
        {
            if (meta.Payload is not null &&
                meta.Payload.TryGetValue(VersionPayloadKey, out var raw) &&
                long.TryParse(raw, out var version))
            {
                return version;
            }

            return -1;
        }
    }
}
