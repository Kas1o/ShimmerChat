using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 轻量级本地化服务。从嵌入资源和文件系统加载 JSON 翻译文件，
    /// 提供 Key → 显示字符串的查找，找不到时回退到 Key 本身。
    /// 语言设置通过 IKVDataService 持久化，切换即时生效。
    /// <para>
    /// 每个程序集可以为同一 culture 提供多份翻译文件：只要嵌入资源位于 Locales/
    /// 目录、文件名以 ".{culture}.json" 结尾（主文件 "zh-CN.json"、按领域拆分的
    /// "render.zh-CN.json" 等），都会经后缀匹配被发现并按键合并。同程序集内按资源名
    /// 排序依次合并，后合并者覆盖先合并者；程序集之间沿用插件加载器的装配顺序。
    /// 解析失败的文件只作废自身：跳过并记录错误，不拖垮整个语言。
    /// </para>
    /// </summary>
    public class LocService : ILocService
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _fallbackCulture = "en-US";
        private readonly IKVDataService _kvData;
        private readonly IPluginLoaderService _pluginLoader;
        private readonly ILogger<LocService>? _logger;
        private readonly string? _overrideDirectory;
        private const string KVSpace = "_locale";
        private const string KVKey = "culture";
        private const string LocaleFolderMarker = "Locales.";

        /// <inheritdoc/>
        public string CurrentCulture { get; private set; }

        /// <summary>支持的区域性列表</summary>
        public static readonly string[] SupportedCultures = ["zh-CN", "en-US"];

        /// <inheritdoc/>
        IReadOnlyList<string> ILocService.SupportedCultures => SupportedCultures;

        public LocService(IKVDataService kvData, IPluginLoaderService pluginLoader,
            string? overrideDirectory = null, ILogger<LocService>? logger = null)
        {
            _kvData = kvData;
            _pluginLoader = pluginLoader;
            _overrideDirectory = overrideDirectory;
            _logger = logger;
            CurrentCulture = LoadSavedCulture();
            ReloadEntries(CurrentCulture);
        }

        /// <inheritdoc/>
        public void SetCulture(string culture)
        {
            if (culture == CurrentCulture) return;

            // 未登记的 culture 不做静默猜测：拒绝切换并留下可追踪日志，当前语言保持不变。
            if (!SupportedCultures.Contains(culture))
            {
                _logger?.LogWarning("[Loc] 拒绝切换到未支持的语言 '{Culture}'，当前语言仍为 '{Current}'",
                    culture, CurrentCulture);
                return;
            }

            CurrentCulture = culture;
            ReloadEntries(culture);
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

        private void ReloadEntries(string culture)
        {
            // 切换语言时覆盖目录同样要重新应用，否则覆盖文件只在首次启动生效。
            _entries.Clear();
            LoadFromEmbeddedResources(culture);
            if (!string.IsNullOrEmpty(_overrideDirectory))
                LoadFromOverrides(_overrideDirectory, culture);
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
                    foreach (var fullName in GetLocaleResourceNames(asm, culture))
                    {
                        using var stream = asm.GetManifestResourceStream(fullName);
                        if (stream == null)
                        {
                            _logger?.LogError("[Loc] 本地化资源 {Resource} 报告存在但无法打开", fullName);
                            continue;
                        }
                        MergeFromStream(stream, fullName);
                    }
                }
                catch (Exception ex)
                {
                    // 枚举程序集资源属于插件边界：单个程序集失败不能拖垮其它程序集的语言加载。
                    _logger?.LogError(ex, "[Loc] 从程序集 {Assembly} 加载语言 {Culture} 失败: {Message}",
                        asm.FullName, culture, ex.Message);
                }
            }
        }

        /// <summary>
        /// 后缀匹配一个程序集内某个 culture 的全部本地化资源：位于 Locales/ 目录、
        /// 文件名以 ".{culture}.json" 结尾（含主文件 "zh-CN.json" 与拆分文件 "tag.zh-CN.json"）。
        /// culture 必须是独立的点分段，避免 "en" 误匹配 "frozen.json" 这类文件名；
        /// 排序让同键冲突的胜者确定可预期（资源名靠后者覆盖靠前者）。
        /// <para>这是本地化文件发现规则的唯一定义：宿主加载、契约测试与文档都引用它。</para>
        /// </summary>
        public static IEnumerable<string> GetLocaleResourceNames(Assembly assembly, string culture)
        {
            var suffix = '.' + culture + ".json";
            return assembly.GetManifestResourceNames()
                .Where(n => IsLocaleResourceName(n, suffix))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsLocaleResourceName(string resourceName, string suffix)
        {
            // 只认领 Locales/ 目录下的资源，避免误捡程序集里其它以 ".{culture}.json" 结尾的嵌入文件；
            // RootNamespace 为空时嵌入名以 "Locales." 开头，否则形如 "{Namespace}.Locales.{文件}"。
            var idx = resourceName.LastIndexOf('.' + LocaleFolderMarker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 && !resourceName.StartsWith(LocaleFolderMarker, StringComparison.OrdinalIgnoreCase))
                return false;

            return resourceName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private void LoadFromOverrides(string directory, string culture)
        {
            if (!Directory.Exists(directory)) return;

            // 与嵌入资源同一后缀规则：先铺 en-US 基线，再用目标 culture 覆盖。
            if (!string.Equals(culture, _fallbackCulture, StringComparison.OrdinalIgnoreCase))
                LoadOverrideFiles(directory, _fallbackCulture);
            LoadOverrideFiles(directory, culture);
        }

        private void LoadOverrideFiles(string directory, string culture)
        {
            var suffix = '.' + culture + ".json";
            var mainName = culture + ".json";
            var files = Directory.EnumerateFiles(directory, "*" + culture + ".json", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return string.Equals(name, mainName, StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    MergeFromJson(File.ReadAllText(file), file);
                }
                catch (Exception ex)
                {
                    // 覆盖目录是外部输入边界：坏文件跳过并显式记录，不吞异常。
                    _logger?.LogError(ex, "[Loc] 读取覆盖文件 {File} 失败: {Message}", file, ex.Message);
                }
            }
        }

        private void MergeFromStream(Stream stream, string source)
        {
            using var reader = new StreamReader(stream);
            MergeFromJson(reader.ReadToEnd(), source);
        }

        private void MergeFromJson(string json, string source)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict == null)
                {
                    _logger?.LogError("[Loc] 本地化文件 {Source} 内容不是键值对象，已跳过", source);
                    return;
                }

                foreach (var (key, value) in dict)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        _entries[key] = value;
                }
            }
            catch (Exception ex)
            {
                // 坏文件只作废自身：跳过并显式记录，不拖垮所在 culture 的其它文件。
                _logger?.LogError(ex, "[Loc] 解析本地化文件 {Source} 失败: {Message}", source, ex.Message);
            }
        }
    }
}
