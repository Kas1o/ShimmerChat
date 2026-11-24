using LiteDB;
using ShimmerChatLib;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Singletons.Storage
{
    /// <summary>
    /// 首次生成 <c>storage_settings.json</c> 时，按旧数据实际所在位置推断每个仓库的后端。
    /// <para>
    /// 旧版只有一个全局开关，且升级时 appsettings.json 可能被发布覆盖；若直接套用默认值，
    /// 原本使用文件后端的用户会看到"数据消失"。这里按旧数据的存在情况逐仓库推断：
    /// LiteDB 里有数据 → LiteDB；LiteDB 为空但旧文件目录有数据 → LocalFileStorage。
    /// </para>
    /// <para>
    /// 同一个仓库两边都有数据时无法判断哪份是当前数据，**不猜测、不迁移**：直接报错并中止启动，
    /// 要求用户显式指定该仓库的 StorageType（或先手工创建 storage_settings.json）。
    /// </para>
    /// </summary>
    public static class LegacyStorageDetector
    {
        public static void ApplyToDefaults(
            StorageSettings defaults,
            string baseDirectory,
            bool legacyConfigPresent,
            Action<string>? log = null)
        {
            ArgumentNullException.ThrowIfNull(defaults);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            if (legacyConfigPresent)
            {
                log?.Invoke("检测到 appsettings.json 的旧存储配置，沿用其中的 StorageType。");
                return;
            }

            var legacyDb = Path.Combine(baseDirectory, "LiteDBKVData", "data.db");
            var kvCount = CountDocuments(legacyDb, "kvdata", "KVData");
            var messageCount = CountDocuments(legacyDb, "messages", "ChatMessage");
            var debugCount = CountDocuments(legacyDb, "debug_output", "DebugOutput");

            var kvDirectory = Path.Combine(baseDirectory, "KVData");
            var messageDirectory = Path.Combine(baseDirectory, "ChatMessages");
            var debugDirectory = Path.Combine(baseDirectory, "DebugOutput");

            ApplyStore(defaults.KVData, StorageStores.KvData, legacyDb, kvCount, kvDirectory, HasDirectories(kvDirectory), log);
            ApplyStore(defaults.ChatMessage, StorageStores.ChatMessage, legacyDb, messageCount, messageDirectory, HasDirectories(messageDirectory), log);
            ApplyStore(defaults.DebugOutput, StorageStores.DebugOutput, legacyDb, debugCount, debugDirectory, HasFiles(debugDirectory, "*.json"), log);
        }

        private static void ApplyStore(
            StoreStorageConfig config,
            string store,
            string databasePath,
            int databaseCount,
            string fileDirectory,
            bool hasFiles,
            Action<string>? log)
        {
            if (databaseCount > 0 && hasFiles)
            {
                throw new InvalidOperationException(
                    $"{store} 同时存在旧 LiteDB 数据（{databasePath}，{databaseCount} 条）和旧文件存储数据（{fileDirectory}），" +
                    "无法判断哪一份是当前数据。为避免覆盖或丢数据，启动已中止：" +
                    $"请先在 appsettings.json 的 KVDataStorage 中显式设置 StorageType，或手工创建 Data/storage_settings.json " +
                    $"并为 {store} 指定 StorageType（LiteDB 或 LocalFileStorage）。");
            }

            if (databaseCount > 0)
            {
                config.StorageType = nameof(KVStorageType.LiteDB);
                log?.Invoke($"{store}: 旧 LiteDB 中有 {databaseCount} 条数据 → LiteDB。");
                return;
            }

            if (hasFiles)
            {
                config.StorageType = nameof(KVStorageType.LocalFileStorage);
                log?.Invoke($"{store}: 旧 LiteDB 为空，旧文件目录有数据 → LocalFileStorage。");
                return;
            }

            log?.Invoke($"{store}: 未发现旧数据，沿用默认后端 {config.StorageType}。");
        }

        private static int CountDocuments(string databasePath, string collection, string store)
        {
            if (!File.Exists(databasePath)) return 0;

            try
            {
                using var database = new LiteDatabase($"Filename={databasePath};Connection=direct");
                return database.GetCollection<BsonDocument>(collection).Count();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"推断 {store} 后端失败：读取旧数据库 {databasePath} 的集合 '{collection}' 出错。{ex.Message}",
                    ex);
            }
        }

        private static bool HasDirectories(string path)
        {
            try
            {
                return Directory.Exists(path) && Directory.EnumerateDirectories(path).Any();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"检查旧目录 {path} 失败：{ex.Message}", ex);
            }
        }

        private static bool HasFiles(string path, string pattern)
        {
            try
            {
                return Directory.Exists(path) && Directory.EnumerateFiles(path, pattern).Any();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"检查旧目录 {path} 失败：{ex.Message}", ex);
            }
        }
    }
}
