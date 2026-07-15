using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class PreGenerationNodeSerializer : IPreGenerationNodeSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<string, Type> _typeMap;
        private readonly ILogger<PreGenerationNodeSerializer> _logger;

        public PreGenerationNodeSerializer(IPluginLoaderService pluginLoader, ILogger<PreGenerationNodeSerializer> logger)
        {
            _logger = logger;
            var types = pluginLoader.GetImplementingTypes(typeof(IPreGenerationNode));
            _typeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                _typeMap[t.FullName!] = t;
                _typeMap[t.Name] = t;
            }

            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                SerializationBinder = new NodeSerializationBinder(_typeMap)
            };
        }

        public string Serialize(IPreGenerationNode root)
        {
            return JsonConvert.SerializeObject(root, typeof(IPreGenerationNode), _settings);
        }

        public IPreGenerationNode? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IPreGenerationNode>(json, _settings); }
            catch (Exception ex)
            {
                _logger.LogError("[PreGenerationNodeSerializer] 反序列化失败: {Message}", ex.Message);
                _logger.LogError(ex, "{StackTrace}", ex.StackTrace);
                return null;
            }
        }

        public IReadOnlyDictionary<string, Type> GetKnownTypes() => _typeMap;

        // ─── ITreeNodeSerializer explicit implementation ───

        string ITreeNodeSerializer.Serialize(ITreeNode root) => Serialize((IPreGenerationNode)root);

        ITreeNode? ITreeNodeSerializer.Deserialize(string json) => Deserialize(json);

        private class NodeSerializationBinder : ISerializationBinder
        {
            private readonly Dictionary<string, Type> _types;

            public NodeSerializationBinder(Dictionary<string, Type> types) => _types = types;

            public Type BindToType(string? assemblyName, string typeName)
            {
                if (_types.TryGetValue(typeName, out var t)) return t;
                var match = _types.Values.FirstOrDefault(x => x.FullName == typeName || x.Name == typeName);
                if (match != null) return match;
                throw new InvalidOperationException($"Unknown node type: {typeName}");
            }

            public void BindToName(Type serializedType, out string? assemblyName, out string typeName)
            {
                assemblyName = null;
                typeName = serializedType.FullName!;
            }
        }
    }
}
