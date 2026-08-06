using LiteDB;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons
{
    public class LiteDBDebugOutputService : IDebugOutputService
    {
        private readonly ILiteCollection<DebugOutputDocument> _collection;

        public class DebugOutputDocument
        {
            public ObjectId Id { get; set; } = ObjectId.NewObjectId();
            public Guid EntryId { get; set; }
            public DateTime Timestamp { get; set; }
            public string Source { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        public LiteDBDebugOutputService(LiteDatabase database)
        {
            _collection = database.GetCollection<DebugOutputDocument>("debug_output");
            _collection.EnsureIndex(x => x.Timestamp);
            _collection.EnsureIndex(x => x.Source);
            _collection.EnsureIndex(x => x.Category);
            _collection.EnsureIndex(x => x.EntryId, true);
        }

        public void Write(string source, string category, string content)
        {
            Console.WriteLine(content);

            var doc = new DebugOutputDocument
            {
                EntryId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Source = source,
                Category = category,
                Content = content
            };
            _collection.Insert(doc);
        }

        public List<DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _collection.Query();

            if (!string.IsNullOrEmpty(sourceFilter))
                query = query.Where(x => x.Source == sourceFilter);
            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(x => x.Category == categoryFilter);
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(x =>
                    x.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    x.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    x.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (from.HasValue)
                query = query.Where(x => x.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(x => x.Timestamp <= to.Value);

            var docs = query
                .OrderByDescending(x => x.Timestamp)
                .Skip(skip)
                .Limit(take)
                .ToList();

            return docs.Select(d => new DebugOutputEntry
            {
                Id = d.EntryId,
                Timestamp = d.Timestamp,
                Source = d.Source,
                Category = d.Category,
                Content = d.Content
            }).ToList();
        }

        public int GetCount(string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null)
        {
            var query = _collection.Query();

            if (!string.IsNullOrEmpty(sourceFilter))
                query = query.Where(x => x.Source == sourceFilter);
            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(x => x.Category == categoryFilter);
            if (!string.IsNullOrEmpty(keyword))
                query = query.Where(x =>
                    x.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    x.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    x.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (from.HasValue)
                query = query.Where(x => x.Timestamp >= from.Value);
            if (to.HasValue)
                query = query.Where(x => x.Timestamp <= to.Value);

            return query.Count();
        }

        public List<string> GetSources()
        {
            return _collection.FindAll()
                .Select(x => x.Source)
                .Distinct()
                .Order()
                .ToList();
        }

        public List<string> GetCategories()
        {
            return _collection.FindAll()
                .Select(x => x.Category)
                .Distinct()
                .Order()
                .ToList();
        }

        public bool DeleteEntry(Guid id)
        {
            return _collection.DeleteMany(x => x.EntryId == id) > 0;
        }

        public int TrimToRecent(int keep)
        {
            var total = _collection.Count();
            if (keep <= 0)
            {
                _collection.DeleteAll();
                return total;
            }

            if (total <= keep)
                return 0;

            // 取第 keep 新的时间戳作为阈值：保留时间不早于该时刻的条目。
            // 并列时间戳可能使实际保留数略多于 keep，可接受。
            var docs = _collection.Query()
                .OrderByDescending(x => x.Timestamp)
                .Limit(keep)
                .ToList();

            if (docs.Count == 0)
                return 0;

            var threshold = docs[^1].Timestamp;
            return _collection.DeleteMany(x => x.Timestamp < threshold);
        }

        public int DeleteOlderThan(DateTime cutoffUtc)
        {
            return _collection.DeleteMany(x => x.Timestamp < cutoffUtc);
        }

        public void ClearAll()
        {
            _collection.DeleteAll();
        }
    }
}
