using System.Text.Json;
using LiteDB;
using ShimmerChatLib;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Singletons.Storage
{
    /// <summary>
    /// 旧目录结构 → <c>Data/</c> 结构的一次性布局迁移。
    /// <para>
    /// 只复制、不删除：旧目录原样保留，用户确认无误后可自行清理。迁移是幂等的（按文件/记录 upsert），
    /// 完成标记写在 <c>Data/.layout_migration</c>；删除该标记即可重新执行。
    /// </para>
    /// </summary>
    public sealed class StorageLayoutMigrator
    {
        /// <summary>当前数据布局版本。</summary>
        public const int CurrentLayoutVersion = 2;

        private const string LegacyLiteDbDirectoryName = "LiteDBKVData";
        private const string LegacyLiteDbFileName = "data.db";
        private const string LegacyKvDataDirectoryName = "KVData";
        private const string LegacyChatMessageDirectoryName = "ChatMessages";
        private const string LegacyDebugOutputDirectoryName = "DebugOutput";
        private const string LegacyMemoryDirectoryName = "Memory";
        private const string LegacyMemoryFileName = "records.db";
        private const string LegacyUserUploadImageDirectoryName = "UserUploadImage";
        private const string LegacyVectorDirectoryName = "VectorStore";
        private const string LegacyMigrationMarkerName = ".migration_marker";

        private readonly string _baseDirectory;
        private readonly DataPaths _paths;
        private readonly Action<string> _log;

        public StorageLayoutMigrator(string baseDirectory, DataPaths paths, Action<string>? log = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
            ArgumentNullException.ThrowIfNull(paths);

            _baseDirectory = Path.GetFullPath(baseDirectory);
            _paths = paths;
            _log = log ?? (_ => { });
        }

        public string LegacyLiteDbPath => Path.Combine(_baseDirectory, LegacyLiteDbDirectoryName, LegacyLiteDbFileName);

        public string LegacyKvDataDirectory => Path.Combine(_baseDirectory, LegacyKvDataDirectoryName);

        public string LegacyChatMessageDirectory => Path.Combine(_baseDirectory, LegacyChatMessageDirectoryName);

        public string LegacyDebugOutputDirectory => Path.Combine(_baseDirectory, LegacyDebugOutputDirectoryName);

        public string LegacyMemoryDatabase => Path.Combine(_baseDirectory, LegacyMemoryDirectoryName, LegacyMemoryFileName);

        public string LegacyUserUploadImageDirectory => Path.Combine(_baseDirectory, LegacyUserUploadImageDirectoryName);

        public string LegacyMigrationMarker => Path.Combine(LegacyKvDataDirectory, LegacyMigrationMarkerName);

        /// <summary>
        /// 执行布局迁移。已完成时返回 false；执行了迁移返回 true。
        /// </summary>
        /// <param name="legacyVectorRootPath">旧配置 <c>VectorStore:RootPath</c>；null 时按旧默认值 <c>VectorStore</c>。</param>
        /// <param name="legacyStorageType">旧配置 <c>KVDataStorage:StorageType</c>；用于给升级用户补齐迁移标记。</param>
        public bool Migrate(string? legacyVectorRootPath, KVStorageType legacyStorageType)
        {
            if (IsCompleted(out var completedVersion))
            {
                _log($"布局迁移已完成（v{completedVersion}），跳过。删除 {_paths.LayoutMigrationMarkerFile} 可重新执行。");
                return false;
            }

            _paths.EnsureDirectories();

            var steps = new List<string>();

            var kvCount = CopyLiteCollection("kvdata", _paths.KvDataDatabase, StorageStores.KvData, steps);
            var messageCount = CopyLiteCollection("messages", _paths.ChatMessageDatabase, StorageStores.ChatMessage, steps);
            var debugCount = CopyLiteCollection("debug_output", _paths.DebugOutputDatabase, StorageStores.DebugOutput, steps);

            var vectorCollectionCount = MergeLegacyVectorDatabases(legacyVectorRootPath, steps);

            CopyFileWithSidecar(
                LegacyMemoryDatabase,
                _paths.MemoryDatabase,
                $"记忆真值库 {LegacyMemoryDatabase} → {_paths.MemoryDatabase}",
                steps);

            if (vectorCollectionCount > 0)
            {
                RewriteMemoryVectorIdentity(
                    ResolveLegacyVectorRoot(legacyVectorRootPath),
                    steps);
            }

            CopyDirectory(
                LegacyKvDataDirectory,
                _paths.LocalKvDataDirectory,
                skipRootFileNames: [LegacyMigrationMarkerName],
                description: $"KV 文件存储 {LegacyKvDataDirectory} → {_paths.LocalKvDataDirectory}",
                steps);

            CopyDirectory(
                LegacyChatMessageDirectory,
                _paths.LocalChatMessageDirectory,
                skipRootFileNames: null,
                description: $"消息文件存储 {LegacyChatMessageDirectory} → {_paths.LocalChatMessageDirectory}",
                steps);

            CopyDirectory(
                LegacyDebugOutputDirectory,
                _paths.LocalDebugOutputDirectory,
                skipRootFileNames: null,
                description: $"调试输出文件存储 {LegacyDebugOutputDirectory} → {_paths.LocalDebugOutputDirectory}",
                steps);

            CopyDirectory(
                LegacyUserUploadImageDirectory,
                _paths.UserUploadImageDirectory,
                skipRootFileNames: null,
                description: $"用户图像 {LegacyUserUploadImageDirectory} → {_paths.UserUploadImageDirectory}",
                steps);

            CopyFileIfMissing(LegacyMigrationMarker, _paths.MigrationMarkerFile, "旧迁移标记", steps);

            // 旧版用一个开关同时决定 KV / 消息 / 调试输出的后端。旧配置为 LiteDB 且旧库里确有数据时，
            // 该仓库在旧版本里以 LiteDB 为准；补标记避免启动时又拿旧文件存储覆盖。
            // 旧库为空的仓库不补标记：让按仓库的自动迁移有机会把旧文件里的数据恢复进 LiteDB。
            if (File.Exists(LegacyLiteDbPath) && legacyStorageType == KVStorageType.LiteDB)
            {
                var marker = new StorageMigrationMarker(_paths, [LegacyMigrationMarker]);
                var marked = new List<string>();

                if (kvCount > 0)
                {
                    marker.MarkMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, kvCount, false);
                    marked.Add(StorageStores.KvData);
                }

                if (messageCount > 0)
                {
                    marker.MarkMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, messageCount, false);
                    marked.Add(StorageStores.ChatMessage);
                }

                if (debugCount > 0)
                {
                    marker.MarkMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, debugCount, false);
                    marked.Add(StorageStores.DebugOutput);
                }

                if (marked.Count > 0)
                    steps.Add($"补齐迁移标记（旧配置为 LiteDB）：{string.Join(", ", marked)}");
            }

            WriteCompletionMarker(steps);

            _log($"布局迁移完成：{steps.Count} 个步骤，标记写入 {_paths.LayoutMigrationMarkerFile}。");
            return true;
        }

        private int CopyLiteCollection(string collection, string targetDatabase, string store, List<string> steps)
        {
            if (!File.Exists(LegacyLiteDbPath))
            {
                _log($"{store}: 旧 LiteDB 不存在（{LegacyLiteDbPath}），跳过。");
                return 0;
            }

            try
            {
                using var legacy = new LiteDatabase($"Filename={LegacyLiteDbPath};Connection=direct");
                var documents = legacy.GetCollection<BsonDocument>(collection).FindAll().ToList();
                if (documents.Count == 0)
                {
                    _log($"{store}: 旧库集合 '{collection}' 为空，跳过。");
                    return 0;
                }

                var directory = Path.GetDirectoryName(targetDatabase);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using var target = new LiteDatabase($"Filename={targetDatabase};Connection=direct");
                var destination = target.GetCollection<BsonDocument>(collection);

                foreach (var document in documents)
                    destination.Upsert(document);

                steps.Add($"{store}: {documents.Count} 条（{LegacyLiteDbPath}#{collection} → {targetDatabase}）");
                _log($"{store}: 已迁移 {documents.Count} 条到 {targetDatabase}。");
                return documents.Count;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"布局迁移失败：{store} 旧库 {LegacyLiteDbPath} 的集合 '{collection}' → {targetDatabase}。{ex.Message}",
                    ex);
            }
        }

        private int MergeLegacyVectorDatabases(string? legacyVectorRootPath, List<string> steps)
        {
            var legacyRoot = ResolveLegacyVectorRoot(legacyVectorRootPath);

            if (!Directory.Exists(legacyRoot))
            {
                _log($"向量库：旧目录不存在（{legacyRoot}），跳过。");
                return 0;
            }

            var targetDirectory = Path.GetDirectoryName(_paths.VectorDatabase)!;
            if (string.Equals(
                    Path.GetFullPath(legacyRoot),
                    Path.GetFullPath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                _log("向量库：旧目录与新目录相同，跳过。");
                return 0;
            }

            var files = Directory.GetFiles(legacyRoot, "*.db")
                .Where(file => !file.EndsWith("-log.db", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                _log($"向量库：旧目录 {legacyRoot} 没有数据库文件，跳过。");
                return 0;
            }

            try
            {
                using var target = new LiteDatabase($"Filename={_paths.VectorDatabase};Connection=direct");
                var targetMeta = target.GetCollection<BsonDocument>("_meta");
                var collectionCount = 0;
                var recordCount = 0;

                foreach (var file in files)
                {
                    using var legacy = new LiteDatabase($"Filename={file};Connection=direct");
                    var metas = legacy.GetCollection<BsonDocument>("_meta").FindAll().ToList();

                    foreach (var meta in metas)
                    {
                        var physical = meta["Physical"].AsString;
                        targetMeta.Upsert(meta);

                        var documents = legacy.GetCollection<BsonDocument>(physical).FindAll().ToList();
                        var destination = target.GetCollection<BsonDocument>(physical);
                        foreach (var document in documents)
                        {
                            destination.Upsert(document);
                            recordCount++;
                        }

                        collectionCount++;
                    }
                }

                if (collectionCount == 0)
                {
                    _log($"向量库：旧目录 {legacyRoot} 没有可迁移的集合。");
                    return 0;
                }

                steps.Add($"向量库: {collectionCount} 个集合 / {recordCount} 条记录（{legacyRoot} → {_paths.VectorDatabase}）");
                _log($"向量库：已合并 {collectionCount} 个集合 / {recordCount} 条记录到 {_paths.VectorDatabase}。");
                return collectionCount;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"布局迁移失败：向量库 {legacyRoot} → {_paths.VectorDatabase}。{ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// 向量数据从旧的分文件目录合并进 vector.db 后，记忆真值库里记录的 StoreIdentity 仍指向旧位置；
        /// 不改写的话记忆索引会被判定为"后端已更换"并要求重建。数据实际就是同一批迁移过来的向量，
        /// 因此把身份改写成新位置即可继续使用。
        /// </summary>
        private void RewriteMemoryVectorIdentity(string legacyVectorRoot, List<string> steps)
        {
            if (!File.Exists(_paths.MemoryDatabase)) return;

            var legacyIdentity = $"LiteDb:{legacyVectorRoot}";
            var newIdentity = $"LiteDb:{_paths.VectorDatabase}";

            try
            {
                using var memory = new LiteDatabase($"Filename={_paths.MemoryDatabase};Connection=direct");
                var states = memory.GetCollection<BsonDocument>("model_state");
                var updated = 0;

                foreach (var state in states.FindAll().ToList())
                {
                    if (!state.TryGetValue("StoreIdentity", out var value) || !value.IsString) continue;
                    if (!string.Equals(value.AsString, legacyIdentity, StringComparison.Ordinal)) continue;

                    state["StoreIdentity"] = newIdentity;
                    states.Update(state);
                    updated++;
                }

                if (updated > 0)
                {
                    steps.Add($"记忆索引身份 {legacyIdentity} → {newIdentity}（{updated} 个模型）");
                    _log($"记忆索引身份已改写为 {newIdentity}（{updated} 个模型）。");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"布局迁移失败：更新记忆索引身份（{_paths.MemoryDatabase}）。{ex.Message}",
                    ex);
            }
        }

        private string ResolveLegacyVectorRoot(string? configuredRoot)
        {
            var relative = string.IsNullOrWhiteSpace(configuredRoot)
                ? LegacyVectorDirectoryName
                : configuredRoot.Trim();

            return Path.IsPathRooted(relative)
                ? Path.GetFullPath(relative)
                : Path.GetFullPath(Path.Combine(_baseDirectory, relative));
        }

        private void CopyDirectory(
            string sourceDirectory,
            string targetDirectory,
            IReadOnlyCollection<string>? skipRootFileNames,
            string description,
            List<string> steps)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                _log($"{description}：源目录不存在，跳过。");
                return;
            }

            var copied = 0;

            foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDirectory, source);

                if (skipRootFileNames is { Count: > 0 } &&
                    !relative.Contains(Path.DirectorySeparatorChar) &&
                    skipRootFileNames.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(targetDirectory, relative);
                if (File.Exists(target)) continue;

                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.Copy(source, target);
                copied++;
            }

            if (copied == 0)
            {
                _log($"{description}：没有需要复制的新文件。");
                return;
            }

            steps.Add($"{description}（{copied} 个文件）");
            _log($"{description}：已复制 {copied} 个文件。");
        }

        private void CopyFileWithSidecar(string source, string target, string description, List<string> steps)
        {
            if (!File.Exists(source))
            {
                _log($"{description}：源文件不存在，跳过。");
                return;
            }

            if (File.Exists(target))
            {
                _log($"{description}：目标已存在，跳过。");
                return;
            }

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.Copy(source, target);
            CopyFileIfMissing(SourceLogFile(source), TargetLogFile(target), description + "（预写日志）", steps);

            steps.Add(description);
            _log($"{description}：已复制。");
        }

        private void CopyFileIfMissing(string source, string target, string description, List<string> steps)
        {
            if (!File.Exists(source)) return;
            if (File.Exists(target)) return;

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.Copy(source, target);
            steps.Add(description);
            _log($"{description}：已复制 {source} → {target}。");
        }

        private static string SourceLogFile(string file) =>
            Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-log.db");

        private static string TargetLogFile(string file) =>
            Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-log.db");

        private bool IsCompleted(out int version)
        {
            version = 0;

            if (!File.Exists(_paths.LayoutMigrationMarkerFile))
                return false;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_paths.LayoutMigrationMarkerFile));
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("LayoutVersion", out var property))
                {
                    throw new InvalidDataException("缺少 LayoutVersion 字段。");
                }

                version = property.GetInt32();
                return version >= CurrentLayoutVersion;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"布局迁移标记损坏：{_paths.LayoutMigrationMarkerFile}。请删除该文件后重新启动。{ex.Message}",
                    ex);
            }
        }

        private void WriteCompletionMarker(List<string> steps)
        {
            var model = new LayoutMarkerModel
            {
                LayoutVersion = CurrentLayoutVersion,
                CompletedAt = DateTimeOffset.UtcNow,
                Steps = steps
            };

            var json = System.Text.Json.JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            var temp = _paths.LayoutMigrationMarkerFile + ".tmp";

            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, _paths.LayoutMigrationMarkerFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private sealed class LayoutMarkerModel
        {
            public int LayoutVersion { get; set; }
            public DateTimeOffset CompletedAt { get; set; }
            public List<string> Steps { get; set; } = [];
        }
    }
}
