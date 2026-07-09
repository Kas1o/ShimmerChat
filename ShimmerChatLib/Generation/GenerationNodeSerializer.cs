using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ShimmerChatLib.Generation
{
    public static class GenerationNodeSerializer
    {
        private static readonly JsonSerializerSettings _settings;
        private static Dictionary<string, Type>? _typeMap;

        static GenerationNodeSerializer()
        {
            BuildTypeMap();
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                SerializationBinder = new NodeSerializationBinder(_typeMap!)
            };
        }

        public static void BuildTypeMap()
        {
            _typeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !a.FullName!.StartsWith("System.") && !a.FullName.StartsWith("Microsoft."));
            foreach (var asm in assemblies)
            {
                try
                {
                    foreach (var type in asm.GetExportedTypes())
                    {
                        if (typeof(IGenerationNode).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                        {
                            _typeMap[type.FullName!] = type;
                            _typeMap[type.Name] = type;
                        }
                    }
                }
                catch { }
            }
        }

        public static string Serialize(IGenerationNode root)
        {
            return JsonConvert.SerializeObject(root, typeof(IGenerationNode), _settings);
        }

        public static IGenerationNode? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return null;
            try { return JsonConvert.DeserializeObject<IGenerationNode>(json, _settings); }
            catch { return null; }
        }

        public static IReadOnlyDictionary<string, Type> GetKnownTypes()
        {
            return _typeMap ?? new Dictionary<string, Type>();
        }

        private class NodeSerializationBinder : ISerializationBinder
        {
            private readonly Dictionary<string, Type> _types;
            public NodeSerializationBinder(Dictionary<string, Type> types) { _types = types; }
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
