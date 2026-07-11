using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class GenerationNodeSerializer : IGenerationNodeSerializer
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<string, Type> _typeMap;

        public GenerationNodeSerializer(IPluginLoaderService pluginLoader)
        {
            var types = pluginLoader.GetImplementingTypes(typeof(IGenerationNode));
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

        public string Serialize(IGenerationNode root)
        {
            return JsonConvert.SerializeObject(root, typeof(IGenerationNode), _settings);
        }

        public IGenerationNode? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IGenerationNode>(json, _settings); }
            catch (Exception ex)
            {
                Console.WriteLine($"[GenerationNodeSerializer] 反序列化失败: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
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
