using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 渲染修改节点树序列化器。扫描所有 IRenderModifierNode 实现构建类型白名单。
    /// </summary>
    public class RenderModifierNodeSerializer : ITreeNodeSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<string, Type> _typeMap;
        private readonly ILogger<RenderModifierNodeSerializer> _logger;

        public RenderModifierNodeSerializer(IPluginLoaderService pluginLoader, ILogger<RenderModifierNodeSerializer> logger)
        {
            _logger = logger;
            var types = pluginLoader.GetImplementingTypes(typeof(IRenderModifierNode));
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
            return JsonConvert.SerializeObject(root, typeof(IRenderModifierNode), _settings);
        }

        public ITreeNode? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IRenderModifierNode>(json, _settings); }
            catch (Exception ex)
            {
                _logger.LogError("[RenderModifierNodeSerializer] 反序列化失败: {Message}", ex.Message);
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
