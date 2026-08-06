using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Newtonsoft.Json;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 插件专用 AssemblyLoadContext，可回收，解决依赖冲突和卸载问题。
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver? _resolver;
        private readonly ILogger<PluginLoadContext> _logger;

        /// <summary>插件在 /pluginstatic/ 下的 URL 段（取自 manifest name，缺失时回退目录名），非法时为空。</summary>
        public string? UrlName { get; private set; }

        /// <summary>插件静态资源目录的绝对路径（manifest "static" 字段解析并校验后），无静态资源或校验失败时为 null。</summary>
        public string? StaticDirPath { get; private set; }

        private static readonly string[] SharedPrefixes =
		[
			"ShimmerChat",
            "SharperLLM",
            "Newtonsoft.Json",
            "System.",
            "Microsoft.",
            "netstandard",
        ];

        public PluginLoadContext(string pluginDir, ILogger<PluginLoadContext> logger)
            : base(name: $"PluginContext_{Path.GetFileName(pluginDir)}", isCollectible: true)
        {
            _logger = logger;
        }

        /// <summary>从 plugin.json manifest 加载入口程序集（以及静态资源目录）。</summary>
        public bool TryLoadFromManifest(string pluginDir)
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) return false;

            PluginManifest? manifest;
            try { manifest = JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ALC] Failed to parse {ManifestPath}: {Message}", manifestPath, ex.Message);
                return false;
            }

            var hasAssembly = !string.IsNullOrWhiteSpace(manifest?.Assembly);
            var hasStatic = !string.IsNullOrWhiteSpace(manifest?.Static);
            if (!hasAssembly && !hasStatic) return false;

            // 解析插件 URL 段：优先 manifest name，sanitize 后无效则回退目录名
            var segment = SanitizeUrlSegment(manifest?.Name ?? "");
            if (!IsValidUrlSegment(segment))
                segment = SanitizeUrlSegment(Path.GetFileName(pluginDir));
            if (!IsValidUrlSegment(segment))
            {
                _logger.LogError(
                    "[ALC] Plugin {PluginDir} has invalid name for URL segment; static resources are disabled.",
                    pluginDir);
            }
            else
            {
                UrlName = segment;
            }

            // 解析静态资源目录（带路径穿越防护，校验失败仅禁用静态资源，不拖垮插件加载）
            if (hasStatic)
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(pluginDir, manifest!.Static!)); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ALC] Plugin {PluginDir} has invalid static path '{Static}': {Message}",
                        pluginDir, manifest!.Static, ex.Message);
                    return false;
                }

                if (!IsPathUnderRoot(full, pluginDir))
                {
                    _logger.LogError(
                        "[ALC] Plugin {PluginDir} static path '{Static}' escapes the plugin directory ({Full}); static resources are disabled.",
                        pluginDir, manifest!.Static, full);
                }
                else if (!Directory.Exists(full))
                {
                    _logger.LogError(
                        "[ALC] Plugin {PluginDir} static directory '{Full}' does not exist; static resources are disabled.",
                        pluginDir, full);
                }
                else
                {
                    StaticDirPath = full;
                }
            }

            // 加载入口程序集
            if (hasAssembly)
            {
                var asmPath = Path.Combine(pluginDir, manifest!.Assembly!);
                if (!File.Exists(asmPath)) return false;

                _resolver = new AssemblyDependencyResolver(asmPath);

                try { LoadFromAssemblyPath(asmPath); }
                catch (Exception ex) { _logger.LogError(ex, "[ALC] Failed to load {AsmPath}: {Message}", asmPath, ex.Message); return false; }
            }

            return true;
        }

        /// <summary>将插件名转换为可安全用于 URL 路径段的字符串：仅保留 ASCII 字母数字与 - _ .。</summary>
        private static string SanitizeUrlSegment(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>URL 段必须非空且不是 "." 或 ".."（防止路径穿越歧义）。</summary>
        private static bool IsValidUrlSegment(string segment)
            => segment.Length > 0 && segment is not "." and not "..";

        /// <summary>判断 <paramref name="fullPath"/> 是否为 <paramref name="root"/> 的严格子目录（按真实目录父子关系，不依赖字符串大小写）。</summary>
        private static bool IsPathUnderRoot(string fullPath, string root)
        {
            var rootFull = Path.GetFullPath(root);
            var current = new DirectoryInfo(fullPath).Parent;
            while (current != null)
            {
                if (string.Equals(
                        current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.Ordinal))
                {
                    return true;
                }
                current = current.Parent;
            }
            return false;
        }

        protected override Assembly? Load(AssemblyName name)
        {
            // 共享程序集走默认上下文，避免类型重复
            if (SharedPrefixes.Any(p => name.Name?.StartsWith(p, StringComparison.OrdinalIgnoreCase) == true))
                return null;

            var path = _resolver?.ResolveAssemblyToPath(name);
            if (path != null)
                return LoadFromAssemblyPath(path);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        private class PluginManifest
        {
            [JsonProperty("name")] public string? Name { get; set; }
            [JsonProperty("version")] public string? Version { get; set; }
            [JsonProperty("description")] public string? Description { get; set; }
            [JsonProperty("assembly")] public string? Assembly { get; set; }
            [JsonProperty("static")] public string? Static { get; set; }
        }
    }
}
