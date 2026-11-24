using System.Text.Json;
using ShimmerChatLib.Models;

namespace ShimmerChatLib.Storage
{
    /// <summary>
    /// <c>Data/storage_settings.json</c> 的读写与校验。
    /// <para>
    /// 该文件是存储配置的唯一事实来源：不存在时由调用方给出默认值并落盘，已存在时只读不覆盖，
    /// 避免悄悄改写用户配置。解析失败、字段缺失或取值非法都会抛错并中止启动，不做静默兜底。
    /// </para>
    /// </summary>
    public sealed class StorageSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public StorageSettingsStore(DataPaths paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            FilePath = paths.StorageSettingsFile;
        }

        public string FilePath { get; }

        /// <summary>
        /// 加载配置；文件不存在时写入 <paramref name="defaults"/> 并返回它。
        /// </summary>
        public StorageSettings LoadOrCreate(StorageSettings defaults, out bool created)
        {
            ArgumentNullException.ThrowIfNull(defaults);

            if (File.Exists(FilePath))
            {
                created = false;
                return Load();
            }

            Validate(defaults);
            Save(defaults);
            created = true;
            return defaults;
        }

        /// <summary>读取并校验配置；文件缺失或内容非法时抛错。</summary>
        public StorageSettings Load()
        {
            if (!File.Exists(FilePath))
                throw new FileNotFoundException($"存储配置文件不存在：{FilePath}", FilePath);

            var json = File.ReadAllText(FilePath);
            EnsureRequiredSections(json);

            StorageSettings settings;
            try
            {
                settings = JsonSerializer.Deserialize<StorageSettings>(json, JsonOptions)
                           ?? throw new InvalidDataException($"存储配置文件内容为空：{FilePath}");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"存储配置文件不是合法 JSON：{FilePath}", ex);
            }

            Validate(settings);
            return settings;
        }

        /// <summary>原子写入配置（临时文件 + 覆盖移动）。</summary>
        public void Save(StorageSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Validate(settings);

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temp = FilePath + ".tmp";

            try
            {
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        /// <summary>
        /// 必填节必须显式出现：缺节意味着配置文件被裁过，不能悄悄用默认值补上。
        /// </summary>
        private void EnsureRequiredSections(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"存储配置文件不是合法 JSON：{FilePath}", ex);
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"存储配置文件根节点必须是对象：{FilePath}。");

                string[] required =
                [
                    "SchemaVersion", "KVData", "ChatMessage", "DebugOutput", "VectorStore"
                ];

                foreach (var name in required)
                {
                    if (!HasProperty(document.RootElement, name))
                    {
                        throw new InvalidOperationException(
                            $"存储配置缺少 {name} 节：{FilePath}。请补全或删除该文件让程序重新生成。");
                    }
                }
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

        private void Validate(StorageSettings settings)
        {
            if (settings.SchemaVersion > StorageSettings.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"存储配置 schemaVersion={settings.SchemaVersion} 高于当前程序支持的版本 " +
                    $"{StorageSettings.CurrentSchemaVersion}：{FilePath}。请升级程序，不要手改降级。");
            }

            if (settings.SchemaVersion < 1)
            {
                throw new InvalidOperationException(
                    $"存储配置 schemaVersion={settings.SchemaVersion} 非法：{FilePath}。");
            }

            ValidateStore("KVData", settings.KVData);
            ValidateStore("ChatMessage", settings.ChatMessage);
            ValidateStore("DebugOutput", settings.DebugOutput);

            if (settings.VectorStore is null)
                throw new InvalidOperationException($"存储配置缺少 VectorStore 节：{FilePath}。");

            settings.VectorStore.GetProvider();
        }

        private void ValidateStore(string name, StoreStorageConfig? config)
        {
            if (config is null)
                throw new InvalidOperationException($"存储配置缺少 {name} 节：{FilePath}。");

            config.GetStorageType();
            config.GetAutoMigrateFromType();
        }
    }
}
