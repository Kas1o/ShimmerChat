using LiteDB;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Singletons
{
    public class DebugOutputService : IDebugOutputService
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

        public DebugOutputService(LiteDatabase database)
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

        public List<DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null)
        {
            var query = _collection.Query();

            if (!string.IsNullOrEmpty(sourceFilter))
                query = query.Where(x => x.Source == sourceFilter);
            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(x => x.Category == categoryFilter);

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

        public int GetCount(string? sourceFilter = null, string? categoryFilter = null)
        {
            var query = _collection.Query();

            if (!string.IsNullOrEmpty(sourceFilter))
                query = query.Where(x => x.Source == sourceFilter);
            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(x => x.Category == categoryFilter);

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

        public void ClearAll()
        {
            _collection.DeleteAll();
        }
    }
}
