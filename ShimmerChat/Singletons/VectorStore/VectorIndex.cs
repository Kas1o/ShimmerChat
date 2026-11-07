using System.Numerics;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons.VectorStore
{
    /// <summary>
    /// 进程内索引条目。Cosine 度量下 Vector 已归一化。
    /// </summary>
    internal sealed class VectorEntry
    {
        public string Id { get; init; } = string.Empty;
        public string? Partition { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public float[] Vector { get; init; } = [];
        public IReadOnlyDictionary<string, string>? Payload { get; init; }
        public string[] Tags { get; init; } = [];
    }

    internal static class VectorEntryFactory
    {
        public const string TagsPayloadKey = "tags";

        public static VectorEntry Create(VectorRecord record, CollectionOptions options)
        {
            var vector = record.Vector.ToArray();
            if (options.Metric == VectorMetric.Cosine)
                vector = VectorMath.Normalize(vector);

            return new VectorEntry
            {
                Id = record.Id,
                Partition = record.Partition,
                CreatedAt = record.CreatedAt == default ? DateTimeOffset.UtcNow : record.CreatedAt,
                Vector = vector,
                Payload = record.Payload,
                Tags = ParseTags(record.Payload)
            };
        }

        public static string[] ParseTags(IReadOnlyDictionary<string, string>? payload)
        {
            if (payload is not null &&
                payload.TryGetValue(TagsPayloadKey, out var raw) &&
                !string.IsNullOrWhiteSpace(raw))
            {
                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return [];
        }
    }

    internal static class VectorFilterMatcher
    {
        public static bool Matches(VectorEntry entry, VectorFilter? filter)
        {
            if (filter is null) return true;

            if (filter.Partition is not null &&
                !string.Equals(entry.Partition, filter.Partition, StringComparison.Ordinal))
            {
                return false;
            }

            if (filter.From is { } from && entry.CreatedAt < from) return false;
            if (filter.To is { } to && entry.CreatedAt > to) return false;

            if (filter.Ids is { Count: > 0 } ids && !ids.Contains(entry.Id, StringComparer.Ordinal))
            {
                return false;
            }

            if (filter.Tags is { Count: > 0 } tags)
            {
                foreach (var tag in tags)
                {
                    if (!entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;
                }
            }

            return true;
        }
    }

    internal static class VectorMath
    {
        public static float[] Normalize(float[] vector)
        {
            double sum = 0;
            for (var i = 0; i < vector.Length; i++) sum += vector[i] * (double)vector[i];

            if (sum <= double.Epsilon) return vector;

            var inverse = (float)(1.0 / Math.Sqrt(sum));
            var result = new float[vector.Length];
            for (var i = 0; i < vector.Length; i++) result[i] = vector[i] * inverse;
            return result;
        }

        public static float Dot(float[] a, float[] b)
        {
            var width = Vector<float>.Count;
            var accumulator = Vector<float>.Zero;
            var i = 0;

            for (; i <= a.Length - width; i += width)
            {
                accumulator += new Vector<float>(a, i) * new Vector<float>(b, i);
            }

            var sum = Vector.Sum(accumulator);
            for (; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        public static float SquaredDistance(float[] a, float[] b)
        {
            var width = Vector<float>.Count;
            var accumulator = Vector<float>.Zero;
            var i = 0;

            for (; i <= a.Length - width; i += width)
            {
                var delta = new Vector<float>(a, i) - new Vector<float>(b, i);
                accumulator += delta * delta;
            }

            var sum = Vector.Sum(accumulator);
            for (; i < a.Length; i++)
            {
                var delta = a[i] - b[i];
                sum += delta * delta;
            }

            return sum;
        }
    }

    /// <summary>
    /// 按 (partition, id) 组织的进程内向量索引。读写用同一把锁，单次检索在毫秒级。
    /// </summary>
    internal sealed class VectorIndex
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, List<VectorEntry>> _partitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VectorEntry> _byId = new(StringComparer.Ordinal);

        public int Count
        {
            get { lock (_sync) return _byId.Count; }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _partitions.Clear();
                _byId.Clear();
            }
        }

        public void AddRange(IEnumerable<VectorEntry> entries)
        {
            lock (_sync)
            {
                foreach (var entry in entries) AddCore(entry);
            }
        }

        public void AddOrReplace(VectorEntry entry)
        {
            lock (_sync) AddCore(entry);
        }

        public void Remove(IReadOnlyCollection<string> ids)
        {
            lock (_sync)
            {
                foreach (var id in ids)
                {
                    if (_byId.TryGetValue(id, out var entry)) RemoveCore(entry);
                }
            }
        }

        public List<string> FindIds(Func<VectorEntry, bool> predicate)
        {
            lock (_sync) return _byId.Values.Where(predicate).Select(x => x.Id).ToList();
        }

        /// <summary>列出记录元数据（不含向量），用于索引对账。</summary>
        public IReadOnlyList<VectorRecordMeta> ListRecords(string? partition)
        {
            lock (_sync)
            {
                IEnumerable<VectorEntry> source = partition is { } key
                    ? (_partitions.TryGetValue(key, out var list) ? list : Array.Empty<VectorEntry>())
                    : _partitions.Values.SelectMany(x => x);

                return source.Select(x => new VectorRecordMeta(x.Id, x.Partition, x.Payload)).ToArray();
            }
        }

        public IReadOnlyList<VectorHit> Search(
            float[] query,
            int topK,
            VectorFilter? filter,
            float minScore,
            VectorMetric metric)
        {
            var prepared = metric == VectorMetric.Cosine ? VectorMath.Normalize(query) : query;

            lock (_sync)
            {
                var top = new TopK(topK);

                foreach (var entry in EnumerateCandidates(filter))
                {
                    if (!VectorFilterMatcher.Matches(entry, filter)) continue;

                    var score = Score(prepared, entry.Vector, metric);
                    if (score >= minScore) top.Add(entry, score);
                }

                return top.ToHits();
            }
        }

        public IReadOnlyList<IReadOnlyList<VectorHit>> SearchBatch(
            IReadOnlyList<float[]> queries,
            int topK,
            VectorFilter? filter,
            float minScore,
            VectorMetric metric)
        {
            var prepared = metric == VectorMetric.Cosine
                ? queries.Select(VectorMath.Normalize).ToArray()
                : queries;

            lock (_sync)
            {
                var tops = new TopK[prepared.Count];
                for (var i = 0; i < tops.Length; i++) tops[i] = new TopK(topK);

                foreach (var entry in EnumerateCandidates(filter))
                {
                    if (!VectorFilterMatcher.Matches(entry, filter)) continue;

                    for (var i = 0; i < prepared.Count; i++)
                    {
                        var score = Score(prepared[i], entry.Vector, metric);
                        if (score >= minScore) tops[i].Add(entry, score);
                    }
                }

                return tops.Select(x => x.ToHits()).ToArray();
            }
        }

        private IEnumerable<VectorEntry> EnumerateCandidates(VectorFilter? filter)
        {
            if (filter?.Partition is { } partition)
            {
                return _partitions.TryGetValue(partition, out var list)
                    ? list
                    : Array.Empty<VectorEntry>();
            }

            return _partitions.Values.SelectMany(x => x);
        }

        private void AddCore(VectorEntry entry)
        {
            if (_byId.TryGetValue(entry.Id, out var old)) RemoveCore(old);

            _byId[entry.Id] = entry;

            var key = entry.Partition ?? string.Empty;
            if (!_partitions.TryGetValue(key, out var list))
            {
                list = [];
                _partitions[key] = list;
            }

            list.Add(entry);
        }

        private void RemoveCore(VectorEntry entry)
        {
            _byId.Remove(entry.Id);

            var key = entry.Partition ?? string.Empty;
            if (!_partitions.TryGetValue(key, out var list)) return;

            list.Remove(entry);
            if (list.Count == 0) _partitions.Remove(key);
        }

        private static float Score(float[] query, float[] vector, VectorMetric metric)
        {
            return metric switch
            {
                VectorMetric.Euclidean => 1f / (1f + MathF.Sqrt(VectorMath.SquaredDistance(query, vector))),
                _ => VectorMath.Dot(query, vector)
            };
        }

        private sealed class TopK
        {
            private readonly VectorEntry[] _entries;
            private readonly float[] _scores;
            private int _count;

            public TopK(int k)
            {
                if (k <= 0) throw new VectorStoreException(VectorStoreErrorCodes.InvalidArgument, "topK 必须大于 0。");
                _entries = new VectorEntry[k];
                _scores = new float[k];
            }

            public void Add(VectorEntry entry, float score)
            {
                if (_count < _entries.Length)
                {
                    var position = _count++;
                    while (position > 0 && _scores[position - 1] < score)
                    {
                        _scores[position] = _scores[position - 1];
                        _entries[position] = _entries[position - 1];
                        position--;
                    }

                    _scores[position] = score;
                    _entries[position] = entry;
                    return;
                }

                if (score <= _scores[^1]) return;

                var index = _entries.Length - 1;
                while (index > 0 && _scores[index - 1] < score)
                {
                    _scores[index] = _scores[index - 1];
                    _entries[index] = _entries[index - 1];
                    index--;
                }

                _scores[index] = score;
                _entries[index] = entry;
            }

            public IReadOnlyList<VectorHit> ToHits()
            {
                var hits = new VectorHit[_count];
                for (var i = 0; i < _count; i++)
                {
                    var entry = _entries[i];
                    hits[i] = new VectorHit(entry.Id, _scores[i], entry.Partition, entry.CreatedAt, entry.Payload);
                }

                return hits;
            }
        }
    }
}
