using System.Reflection;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Generation
{
    public record ToolMetadata(string NameKey, string DescriptionKey, string[] CategoryKeys, Type Type)
    {
        public string TypeName => Type.FullName!;
    }

    /// <summary>
    /// IAutoCreateToolV2 实现的集中式缓存。扫描委托给 <see cref="IPluginLoaderService"/>。
    /// </summary>
    public class ToolRegistry : IToolRegistry
    {
        private readonly Lazy<IReadOnlyList<ToolMetadata>> _tools;
        private readonly ILogger<ToolRegistry> _logger;

        public ToolRegistry(IPluginLoaderService pluginLoader, ILogger<ToolRegistry> logger)
        {
            _logger = logger;
            _tools = new Lazy<IReadOnlyList<ToolMetadata>>(() => ScanAll(pluginLoader, _logger));
        }

        public IReadOnlyList<ToolMetadata> AllTools => _tools.Value;

        public ToolMetadata? FindByName(string name) =>
            _tools.Value.FirstOrDefault(t => t.NameKey == name);

        public ToolMetadata? FindByTypeName(string typeName) =>
            _tools.Value.FirstOrDefault(t => t.TypeName == typeName);

        public IAutoCreateToolV2? CreateInstance(string typeName, PersistentEnv env)
        {
            var meta = FindByTypeName(typeName);
            return meta == null ? null : CreateInstance(meta.Type, env);
        }

        public IAutoCreateToolV2? CreateInstance(Type type, PersistentEnv env)
        {
            var createMethod = type.GetMethod("Create",
                BindingFlags.Public | BindingFlags.Static);
            if (createMethod == null) return null;

            return (IAutoCreateToolV2)createMethod.Invoke(null, [env])!;
        }

        private static List<ToolMetadata> ScanAll(IPluginLoaderService pluginLoader, ILogger<ToolRegistry> logger)
        {
            var types = pluginLoader.GetImplementingTypes(typeof(IAutoCreateToolV2));
            var result = new List<ToolMetadata>(types.Count);

            var flags = BindingFlags.Public | BindingFlags.Static;
            foreach (var type in types)
            {
                try
                {
                    var nameProp = type.GetProperty("NameKey", flags);
                    if (nameProp == null) continue;
                    var name = (string)nameProp.GetValue(null)!;

                    var descProp = type.GetProperty("DescriptionKey", flags);
                    var desc = descProp != null ? (string)descProp.GetValue(null)! : "";

                    var catProp = type.GetProperty("CategoryKeys", flags);
                    var cat = catProp != null ? (string[])catProp.GetValue(null)! : [];

                    result.Add(new ToolMetadata(name, desc, cat, type));
                }
                catch(Exception ex)
                {
                    logger.LogError(ex, "AutoCreateTool Error({TypeName}): {Ex}", type.FullName, ex);
                }
            }
            return result;
        }
    }
}
