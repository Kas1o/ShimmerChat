using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using System.Text.Json;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 本地文件存储的调试输出服务：每条输出持久化为 DebugOutput/{EntryId:N}.json 文件。
    /// 与 LiteDBDebugOutputService 行为等价，通过 KVDataStorage 配置选择使用。
    /// </summary>
    public class FileSystemDebugOutputService : IDebugOutputService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

        private readonly string root;
        private readonly ILogger<FileSystemDebugOutputService> _logger;

        /// <summary>
        /// 获取根目录路径
        /// </summary>
        public string RootPath => root;

        /// <summary>
        /// 初始化 FileSystemDebugOutputService 实例
        /// </summary>
        public FileSystemDebugOutputService(ILogger<FileSystemDebugOutputService> logger)
        {
            root = Path.Combine(AppContext.BaseDirectory, "DebugOutput");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            _logger = logger;
        }

        /// <summary>
        /// 获取指定条目的文件路径
        /// </summary>
        private string GetEntryFilePath(Guid id)
        {
            return Path.Combine(root, id.ToString("N") + ".json");
        }

        /// <summary>
        /// 读取全部条目；单个文件损坏时记录完整错误日志后跳过，不拖垮其余条目。
        /// </summary>
        private List<DebugOutputEntry> ReadAllEntries()
        {
            var entries = new List<DebugOutputEntry>();
            if (!Directory.Exists(root))
                return entries;

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate debug output files: {Message}", ex.Message);
                return entries;
            }

            foreach (var file in files)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<DebugOutputEntry>(File.ReadAllText(file), JsonOptions);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read debug output file {File}: {Message}", file, ex.Message);
                }
            }

            return entries;
        }

        public void Write(string source, string category, string content)
        {
            Console.WriteLine(content);

            try
            {
                var entry = new DebugOutputEntry
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Source = source,
                    Category = category,
                    Content = content
                };
                File.WriteAllText(GetEntryFilePath(entry.Id), JsonSerializer.Serialize(entry, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write debug output entry: {Message}", ex.Message);
                throw;
            }
        }

        public List<DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null)
        {
            return ReadAllEntries()
                .Where(e => string.IsNullOrEmpty(sourceFilter) || e.Source == sourceFilter)
                .Where(e => string.IsNullOrEmpty(categoryFilter) || e.Category == categoryFilter)
                .OrderByDescending(e => e.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public int GetCount(string? sourceFilter = null, string? categoryFilter = null)
        {
            return ReadAllEntries()
                .Count(e =>
                    (string.IsNullOrEmpty(sourceFilter) || e.Source == sourceFilter) &&
                    (string.IsNullOrEmpty(categoryFilter) || e.Category == categoryFilter));
        }

        public List<string> GetSources()
        {
            return ReadAllEntries()
                .Select(e => e.Source)
                .Distinct()
                .Order()
                .ToList();
        }

        public List<string> GetCategories()
        {
            return ReadAllEntries()
                .Select(e => e.Category)
                .Distinct()
                .Order()
                .ToList();
        }

        public bool DeleteEntry(Guid id)
        {
            try
            {
                string filePath = GetEntryFilePath(id);
                if (!File.Exists(filePath))
                    return false;

                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete debug output entry {Id}: {Message}", id, ex.Message);
                throw;
            }
        }

        public void ClearAll()
        {
            if (!Directory.Exists(root))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(root, "*.json"))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear debug output entries: {Message}", ex.Message);
                throw;
            }
        }
    }
}
