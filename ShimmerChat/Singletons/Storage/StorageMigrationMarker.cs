using System.Text.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Singletons.Storage
{
    /// <summary>
    /// 按数据仓库记录迁移状态。
    /// <para>
    /// 新标记文件是 <c>Data/.migration_marker</c>；旧版是 <c>KVData/.migration_marker</c>（单对象、
    /// 只描述 KV 仓库）。旧文件只读兼容，新写入一律落到新路径，避免两个事实来源继续增长。
    /// </para>
    /// </summary>
    public sealed class StorageMigrationMarker : IStorageMigrationMarker
    {
        private const int MarkerFileVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly string _markerFile;
        private readonly IReadOnlyList<string> _legacyMarkerFiles;
        private readonly object _sync = new();
        private List<StorageMigrationRecord>? _records;

        public StorageMigrationMarker(DataPaths paths, IEnumerable<string>? legacyMarkerFiles = null)
        {
            ArgumentNullException.ThrowIfNull(paths);
            _markerFile = paths.MigrationMarkerFile;
            _legacyMarkerFiles = legacyMarkerFiles?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [];
        }

        public string MarkerFile => _markerFile;

        public bool IsMigrationCompleted(string store, KVStorageType sourceType, KVStorageType targetType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(store);

            return LoadRecords().Any(record =>
                string.Equals(record.Store, store, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.SourceStorage, sourceType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.TargetStorage, targetType.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public void MarkMigrationCompleted(
            string store,
            KVStorageType sourceType,
            KVStorageType targetType,
            int migratedCount,
            bool sourceCleared)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(store);

            lock (_sync)
            {
                var records = LoadRecordsCore();

                records.RemoveAll(record =>
                    string.Equals(record.Store, store, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.SourceStorage, sourceType.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.TargetStorage, targetType.ToString(), StringComparison.OrdinalIgnoreCase));

                records.Add(new StorageMigrationRecord
                {
                    Store = store,
                    SourceStorage = sourceType.ToString(),
                    TargetStorage = targetType.ToString(),
                    MigratedCount = migratedCount,
                    SourceCleared = sourceCleared,
                    MigrationTime = DateTime.UtcNow
                });

                SaveCore(records);
                _records = records;
            }
        }

        public IReadOnlyList<StorageMigrationRecord> GetRecords() => LoadRecords().ToArray();

        public void ClearMarker()
        {
            lock (_sync)
            {
                foreach (var file in new[] { _markerFile }.Concat(_legacyMarkerFiles))
                {
                    if (File.Exists(file)) File.Delete(file);
                }

                _records = [];
            }
        }

        private List<StorageMigrationRecord> LoadRecords()
        {
            lock (_sync)
            {
                return _records ??= LoadRecordsCore();
            }
        }

        private List<StorageMigrationRecord> LoadRecordsCore()
        {
            var candidates = new[] { _markerFile }.Concat(_legacyMarkerFiles);

            foreach (var file in candidates)
            {
                if (!File.Exists(file)) continue;

                var json = File.ReadAllText(file);
                return Parse(json, file);
            }

            return [];
        }

        private static List<StorageMigrationRecord> Parse(string json, string file)
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"迁移标记文件根节点必须是对象：{file}");

                if (HasProperty(document.RootElement, "Migrations"))
                {
                    var model = JsonSerializer.Deserialize<MarkerFileModel>(json, JsonOptions)
                                ?? throw new InvalidDataException($"迁移标记文件内容为空：{file}");
                    return model.Migrations ?? [];
                }

                // 旧版格式：单个对象，只描述 KV 仓库的迁移方向。
                var legacy = JsonSerializer.Deserialize<MigrationMarkerInfo>(json, JsonOptions)
                             ?? throw new InvalidDataException($"迁移标记文件内容为空：{file}");

                if (string.IsNullOrWhiteSpace(legacy.SourceStorage) || string.IsNullOrWhiteSpace(legacy.TargetStorage))
                    throw new InvalidDataException($"旧版迁移标记缺少 source/target：{file}");

                return
                [
                    new StorageMigrationRecord
                    {
                        Store = StorageStores.KvData,
                        SourceStorage = legacy.SourceStorage,
                        TargetStorage = legacy.TargetStorage,
                        MigratedCount = legacy.MigratedCount,
                        SourceCleared = legacy.SourceCleared,
                        MigrationTime = legacy.MigrationTime
                    }
                ];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"迁移标记文件不是合法 JSON：{file}", ex);
            }
        }

        private void SaveCore(List<StorageMigrationRecord> records)
        {
            var directory = Path.GetDirectoryName(_markerFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(
                new MarkerFileModel { Version = MarkerFileVersion, Migrations = records },
                JsonOptions);

            var temp = _markerFile + ".tmp";
            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, _markerFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static bool HasProperty(JsonElement element, string name)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private sealed class MarkerFileModel
        {
            public int Version { get; set; } = MarkerFileVersion;
            public List<StorageMigrationRecord>? Migrations { get; set; } = [];
        }
    }
}
