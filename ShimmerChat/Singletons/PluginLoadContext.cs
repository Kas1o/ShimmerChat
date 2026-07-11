using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 插件专用 AssemblyLoadContext，可回收，解决依赖冲突和卸载问题。
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver? _resolver;

        private static readonly HashSet<string> SharedPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ShimmerChatLib",
            "SharperLLM",
            "Newtonsoft.Json",
            "System.",
            "Microsoft.",
            "netstandard",
        };

        public PluginLoadContext(string pluginDir)
            : base(name: $"PluginContext_{Path.GetFileName(pluginDir)}", isCollectible: true)
        {
        }

        /// <summary>从 plugin.json manifest 加载入口程序集。</summary>
        public bool TryLoadFromManifest(string pluginDir)
        {
            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            if (!File.Exists(manifestPath)) return false;

            PluginManifest? manifest;
            try { manifest = JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(manifestPath)); }
            catch (Exception ex)
            {
                Console.WriteLine($"[ALC] Failed to parse {manifestPath}: {ex.Message}");
                return false;
            }

            if (manifest?.Assembly == null) return false;

            var asmPath = Path.Combine(pluginDir, manifest.Assembly);
            if (!File.Exists(asmPath)) return false;

            _resolver = new AssemblyDependencyResolver(asmPath);

            try { LoadFromAssemblyPath(asmPath); }
            catch (Exception ex) { Console.WriteLine($"[ALC] Failed to load {asmPath}: {ex.Message}"); return false; }

            return true;
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
        }
    }
}
