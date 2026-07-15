using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 后生成节点树序列化器。扫描所有 IPostGenerationNode 实现构建类型白名单。
    /// </summary>
    public class PostGenerationNodeSerializer : IPostGenerationNodeSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<string, Type> _typeMap;
        private readonly ILogger<PostGenerationNodeSerializer> _logger;

        public PostGenerationNodeSerializer(IPluginLoaderService pluginLoader, ILogger<PostGenerationNodeSerializer> logger)
        {
            _logger = logger;
            var types = pluginLoader.GetImplementingTypes(typeof(IPostGenerationNode));
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

        public string Serialize(ITreeNode root)
        {
            return JsonConvert.SerializeObject(root, typeof(IPostGenerationNode), _settings);
        }

        public string Serialize(IPostGenerationNode root)
        {
            return JsonConvert.SerializeObject(root, typeof(IPostGenerationNode), _settings);
        }

        public ITreeNode? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IPostGenerationNode>(json, _settings); }
            catch (Exception ex)
            {
                _logger.LogError("[PostGenerationNodeSerializer] 反序列化失败: {Message}", ex.Message);
                return null;
            }
        }

        IPostGenerationNode? IPostGenerationNodeSerializer.Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IPostGenerationNode>(json, _settings); }
            catch (Exception ex)
            {
                _logger.LogError("[PostGenerationNodeSerializer] 反序列化失败: {Message}", ex.Message);
                return null;
            }
        }

        public IReadOnlyDictionary<string, Type> GetKnownTypes() => _typeMap;

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
