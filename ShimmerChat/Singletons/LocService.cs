using System.Reflection;
using System.Text.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 轻量级本地化服务。从嵌入资源和文件系统加载 JSON 翻译文件，
    /// 提供 Key → 显示字符串的查找，找不到时回退到 Key 本身。
    /// 语言设置通过 IKVDataService 持久化，切换即时生效。
    /// </summary>
    public class LocService : ILocService
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _fallbackCulture = "en-US";
        private readonly IKVDataService _kvData;
        private readonly IPluginLoaderService _pluginLoader;
        private const string KVSpace = "_locale";
        private const string KVKey = "culture";

        /// <inheritdoc/>
        public string CurrentCulture { get; private set; }

        /// <summary>支持的区域性列表</summary>
        public static readonly string[] SupportedCultures = ["zh-CN", "en-US"];

        /// <inheritdoc/>
        IReadOnlyList<string> ILocService.SupportedCultures => SupportedCultures;

        public LocService(IKVDataService kvData, IPluginLoaderService pluginLoader, string? overrideDirectory = null)
        {
            _kvData = kvData;
            _pluginLoader = pluginLoader;
            CurrentCulture = LoadSavedCulture();
            LoadFromEmbeddedResources(CurrentCulture);
            if (!string.IsNullOrEmpty(overrideDirectory))
                LoadFromOverrides(overrideDirectory, CurrentCulture);
        }

        /// <inheritdoc/>
        public void SetCulture(string culture)
        {
            if (culture == CurrentCulture) return;
            CurrentCulture = culture;
            _entries.Clear();
            LoadFromEmbeddedResources(culture);
            _kvData.Write(KVSpace, KVKey, culture);
        }

        private string LoadSavedCulture()
        {
            var saved = _kvData.Read(KVSpace, KVKey);
            if (saved != null && SupportedCultures.Contains(saved))
                return saved;

            var fallback = SupportedCultures[0]; // zh-CN
            _kvData.Write(KVSpace, KVKey, fallback);
            return fallback;
        }

        /// <inheritdoc/>
        public string this[string key] => _entries.TryGetValue(key, out var value) ? value : key;

        /// <inheritdoc/>
        public string Format(string key, params object[] args)
        {
            var template = this[key];
            return string.Format(template, args);
        }

        /// <inheritdoc/>
        public string Format(string key, params (string name, object value)[] args)
        {
            var template = this[key];
            foreach (var (name, value) in args)
                template = template.Replace($"{{{name}}}", value?.ToString() ?? "");
            return template;
        }

        private void LoadFromEmbeddedResources(string culture)
        {
            // Load en-US first as base, then overlay the target culture
            if (!string.Equals(culture, _fallbackCulture, StringComparison.OrdinalIgnoreCase))
                LoadCultureFromAssemblies(_fallbackCulture);

            LoadCultureFromAssemblies(culture);
        }

        private void LoadCultureFromAssemblies(string culture)
        {
            foreach (var asm in _pluginLoader.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var resourceName = $"Locales.{culture}.json";
                    var fullName = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
                    if (fullName == null) continue;

                    using var stream = asm.GetManifestResourceStream(fullName);
                    if (stream == null) continue;
                    MergeFromStream(stream);
                }
                catch { }
            }
        }

        private void LoadFromOverrides(string directory, string culture)
        {
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    MergeFromJson(json);
                }
                catch { }
            }
        }

        private void MergeFromStream(Stream stream)
        {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            MergeFromJson(json);
        }

        private void MergeFromJson(string json)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null) return;
                foreach (var (key, value) in dict)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        _entries[key] = value;
                }
            }
            catch { }
        }
    }
}
