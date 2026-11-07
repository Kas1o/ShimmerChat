using LiteDB;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>记忆真值记录：文本、标签、版本。向量只是它的派生物。</summary>
    public sealed class MemoryRecord
    {
        public string Id { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public long Version { get; set; } = 1;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? Source { get; set; }
    }

    /// <summary>某个模型索引的构建状态。</summary>
    public sealed class MemoryModelState
    {
        /// <summary>modelKey，主键。</summary>
        public string Id { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        /// <summary>构建该索引时使用的向量存储身份；为空表示旧状态，跳过身份校验。</summary>
        public string StoreIdentity { get; set; } = string.Empty;
        public int Dimensions { get; set; }
        public long BuiltRevision { get; set; }
        public int Count { get; set; }
        public DateTimeOffset? BuiltAt { get; set; }
        public string Status { get; set; } = MemoryStateStatus.Missing;
    }

    public static class MemoryStateStatus
    {
        public const string Missing = "missing";
        public const string Ready = "ready";
        public const string Suspect = "suspect";
        public const string Partial = "partial";
        public const string Error = "error";
    }

    internal sealed class MemoryMeta
    {
        public string Id { get; set; } = "meta";
        public long Revision { get; set; }
    }

    /// <summary>
    /// 记忆真值库（LiteDB 单文件）。与向量库分开：删掉向量目录仍可从文本重建。
    /// </summary>
    public sealed class MemoryRecordStore : IDisposable
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<MemoryRecord> _records;
        private readonly ILiteCollection<MemoryModelState> _states;
        private readonly ILiteCollection<MemoryMeta> _meta;
        private readonly object _sync = new();

        public MemoryRecordStore(string path)
        {
            FilePath = path;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            _database = new LiteDatabase($"Filename={path};Connection=direct");
            _records = _database.GetCollection<MemoryRecord>("records");
            _states = _database.GetCollection<MemoryModelState>("model_state");
            _meta = _database.GetCollection<MemoryMeta>("meta");

            _records.EnsureIndex(x => x.AgentId);
            _records.EnsureIndex(x => x.UpdatedAt);

            if (_meta.FindById("meta") is null)
                _meta.Insert(new MemoryMeta());
        }

        public string FilePath { get; }

        public long Revision
        {
            get
            {
                lock (_sync) return _meta.FindById("meta")?.Revision ?? 0;
            }
        }

        public long BumpRevision()
        {
            lock (_sync)
            {
                var meta = _meta.FindById("meta") ?? new MemoryMeta();
                meta.Revision++;
                _meta.Upsert(meta);
                return meta.Revision;
            }
        }

        public MemoryRecord? Get(string id)
        {
            return _records.FindById(id);
        }

        public MemoryRecord Add(string agentId, string content, string tags, string? source)
        {
            var now = DateTimeOffset.UtcNow;
            var record = new MemoryRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                AgentId = agentId,
                Content = content,
                Tags = tags,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
                Source = source
            };

            _records.Insert(record);
            BumpRevision();
            return record;
        }

        /// <summary>更新内容或标签；内容变化时版本递增。返回更新后的记录。</summary>
        public MemoryRecord Update(string id, string? content, string? tags)
        {
            var record = _records.FindById(id)
                ?? throw new KeyNotFoundException($"记忆 '{id}' 不存在。");

            var contentChanged = content is not null && !string.Equals(content, record.Content, StringComparison.Ordinal);

            if (contentChanged) record.Content = content!;
            if (tags is not null) record.Tags = tags;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            if (contentChanged) record.Version++;

            _records.Update(record);
            BumpRevision();
            return record;
        }

        public bool Delete(string id)
        {
            var removed = _records.Delete(id);
            if (removed) BumpRevision();
            return removed;
        }

        public int DeleteByAgent(string agentId)
        {
            var deleted = _records.DeleteMany(x => x.AgentId == agentId);
            if (deleted > 0) BumpRevision();
            return deleted;
        }

        public int Count(string agentId) => _records.Count(x => x.AgentId == agentId);

        /// <summary>按关键词过滤后的条数，供面板分页使用。</summary>
        public int Count(string agentId, string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return Count(agentId);

            return _records.Find(x => x.AgentId == agentId)
                .Count(x => x.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        public int TotalCount() => _records.Count();

        public IReadOnlyList<MemoryRecord> List(string agentId, int offset, int limit, string? keyword = null, bool newestFirst = true)
        {
            IEnumerable<MemoryRecord> query = _records.Find(x => x.AgentId == agentId);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            query = newestFirst
                ? query.OrderByDescending(x => x.UpdatedAt)
                : query.OrderBy(x => x.UpdatedAt);

            return query.Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList();
        }

        /// <summary>索引对账用：返回全部记录的 id/版本/内容。</summary>
        public IReadOnlyList<MemoryRecord> AllForIndex() => _records.FindAll().ToList();

        public IReadOnlyList<MemoryModelState> ListStates() => _states.FindAll().ToList();

        public MemoryModelState? GetState(string modelKey) => _states.FindById(modelKey);

        public void SaveState(MemoryModelState state) => _states.Upsert(state);

        public void RemoveState(string modelKey) => _states.Delete(modelKey);

        public void Dispose() => _database.Dispose();
    }

    /// <summary>进程级共享的记忆真值库。</summary>
    public static class MemoryStoreProvider
    {
        private static readonly Lazy<MemoryRecordStore> LazyStore = new(() =>
            new MemoryRecordStore(Path.Combine(AppContext.BaseDirectory, "Memory", "records.db")));

        private static MemoryRecordStore? _override;

        public static MemoryRecordStore Current => _override ?? LazyStore.Value;

        /// <summary>
        /// 临时替换进程级存储（测试与嵌入宿主用）。返回的 scope 释放后恢复原值。
        /// </summary>
        public static IDisposable Override(MemoryRecordStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            var previous = _override;
            _override = store;
            return new OverrideScope(() => _override = previous);
        }

        private sealed class OverrideScope(Action restore) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) restore();
            }
        }
    }
}
