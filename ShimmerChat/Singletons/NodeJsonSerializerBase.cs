using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 节点树 JSON 序列化公共实现：类型白名单、包装器树读写、旧格式懒迁移。
    /// 三个管线的序列化器只在根包装器类型与节点接口上不同。
    /// </summary>
    public abstract class NodeJsonSerializerBase
    {
        private readonly JsonSerializerSettings _settings;
        private readonly Dictionary<string, Type> _typeMap;
        private readonly ILogger _logger;

        /// <summary>根包装器的具体类型（PreGenerationNodeWrapper 等）。</summary>
        protected abstract Type RootWrapperType { get; }

        protected NodeJsonSerializerBase(IPluginLoaderService pluginLoader, Type nodeInterfaceType, ILogger logger)
        {
            _logger = logger;

            var types = pluginLoader.GetImplementingTypes(nodeInterfaceType);
            _typeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                _typeMap[t.FullName!] = t;
                _typeMap[t.Name] = t;
            }

            // TypeNameHandling.Objects 也会给包装器写出 $type；白名单必须认识它们，
            // 否则新格式 JSON 会被当成未知类型拒绝。
            foreach (var wrapperType in new[]
                     {
                         typeof(PreGenerationNodeWrapper),
                         typeof(PostGenerationNodeWrapper),
                         typeof(RenderModifierNodeWrapper)
                     })
            {
                _typeMap[wrapperType.FullName!] = wrapperType;
                _typeMap[wrapperType.Name] = wrapperType;
            }

            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                SerializationBinder = new NodeSerializationBinder(_typeMap)
            };
        }

        public IReadOnlyDictionary<string, Type> GetKnownTypes() => _typeMap;

        protected string SerializeCore(ITreeNode root)
        {
            if (!RootWrapperType.IsInstanceOfType(root))
                throw new ArgumentException(
                    $"Root must be a {RootWrapperType.Name}, got {root.GetType().Name}.",
                    nameof(root));

            return JsonConvert.SerializeObject(root, RootWrapperType, _settings);
        }

        protected NodeDeserializationResult DeserializeCore(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return NodeDeserializationResult.Empty();

            JObject rootObject;
            try
            {
                if (JToken.Parse(json) is not JObject parsed)
                    return Fail("Node tree JSON root must be an object.");

                rootObject = parsed;
            }
            catch (JsonException ex)
            {
                return Fail($"Node tree JSON is not valid JSON: {ex.Message}", ex);
            }

            var migrated = false;
            try
            {
                if (NodeTreeJsonMigration.IsLegacy(rootObject))
                {
                    // 旧格式：业务节点对象直接作为树节点，Id/Name 写在节点上。
                    rootObject = NodeTreeJsonMigration.MigrateLegacyTree(
                        rootObject, RootWrapperType, _typeMap);
                    migrated = true;
                    _logger.LogInformation(
                        "[{Serializer}] Migrated legacy node tree JSON to wrapper format (wrapper: {Wrapper}).",
                        GetType().Name, RootWrapperType.Name);
                }

                var serializer = JsonSerializer.Create(_settings);
                if (rootObject.ToObject(RootWrapperType, serializer) is not ITreeNode wrapper)
                    return Fail("Node tree JSON did not produce a wrapper node.");

                if (wrapper.Payload == null)
                    return Fail($"Node wrapper '{wrapper.Name}' ({wrapper.Id}) has no payload.");

                return NodeDeserializationResult.Ok(wrapper, migrated);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message, ex);
            }
        }

        private NodeDeserializationResult Fail(string message, Exception? ex = null)
        {
            if (ex != null)
                _logger.LogError(ex, "[{Serializer}] 节点树反序列化失败: {Message}", GetType().Name, message);
            else
                _logger.LogError("[{Serializer}] 节点树反序列化失败: {Message}", GetType().Name, message);

            return NodeDeserializationResult.Failed(message);
        }

        private sealed class NodeSerializationBinder : ISerializationBinder
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
