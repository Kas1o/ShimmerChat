using ShimmerChatLib.ChatView;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Tests.SubAgent;

namespace ShimmerChatBuiltin.Tests.Creator;

/// <summary>
/// Creator 工具集的测试桩：内存 KV、按类型映射的服务容器、可配置的序列化器 / 节点目录 /
/// 对话界面注册表，以及返回 Key 本身的本地化服务。
/// </summary>
internal static class CreatorTestDoubles
{
    /// <summary>返回 Key 本身的本地化桩；带参数的 Key 回显参数，便于断言。</summary>
    internal sealed class KeyLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN", "en-US"];

        public string this[string key] => key;

        public string Format(string key, params object[] args) => $"{key}|{string.Join(",", args)}";

        public string Format(string key, params (string name, object value)[] args) => key;

        public void SetCulture(string culture) { }
    }

    /// <summary>只做类型 → 实例映射的服务容器桩，避免测试工程额外引入 DI 包。</summary>
    internal sealed class FakeServices : IServiceProvider
    {
        private readonly Dictionary<Type, object?> _map = new();

        public FakeServices With<T>(T? instance)
        {
            _map[typeof(T)] = instance;
            return this;
        }

        public object? GetService(Type serviceType)
            => _map.TryGetValue(serviceType, out var value) ? value : null;
    }

    /// <summary>
    /// 可配置的节点树序列化器桩，同时实现三条管线的序列化器接口，
    /// 便于按管线路由的服务解析（CreatorShared.ResolveSerializer）拿到同一个桩。
    /// 测试用返回值控制「校验通过 / 校验失败 / 空树」三条路径。
    /// </summary>
    internal sealed class StubTreeSerializer :
        IPreGenerationNodeSerializer,
        IPostGenerationNodeSerializerService,
        IRenderModifierNodeSerializer
    {
        public Func<string, NodeDeserializationResult> OnDeserializeTree { get; set; }
            = _ => NodeDeserializationResult.Empty();

        public Func<ITreeNode, string> OnSerialize { get; set; }
            = _ => "{\"normalized\":true}";

        public string Serialize(ITreeNode root) => OnSerialize(root);

        public ITreeNode? Deserialize(string json) => DeserializeTree(json).Root;

        public NodeDeserializationResult DeserializeTree(string json) => OnDeserializeTree(json);

        public IReadOnlyDictionary<string, Type> GetKnownTypes() => new Dictionary<string, Type>();

        // 三条管线的类型安全重载：统一走桩的配置路径。
        string IPreGenerationNodeSerializer.Serialize(PreGenerationNodeWrapper root) => Serialize(root);

        PreGenerationNodeWrapper? IPreGenerationNodeSerializer.Deserialize(string json)
            => DeserializeTree(json).Root as PreGenerationNodeWrapper;

        string IPostGenerationNodeSerializerService.Serialize(PostGenerationNodeWrapper root) => Serialize(root);

        PostGenerationNodeWrapper? IPostGenerationNodeSerializerService.Deserialize(string json)
            => DeserializeTree(json).Root as PostGenerationNodeWrapper;

        string IRenderModifierNodeSerializer.Serialize(RenderModifierNodeWrapper root) => Serialize(root);

        RenderModifierNodeWrapper? IRenderModifierNodeSerializer.Deserialize(string json)
            => DeserializeTree(json).Root as RenderModifierNodeWrapper;
    }

    /// <summary>节点目录桩：注册时给定的元数据按管线接口过滤，与真实目录行为一致。</summary>
    internal sealed class StubNodeCatalog : INodeTypeCatalog
    {
        public List<NodeTypeMetadata> Types { get; } = new();

        public IReadOnlyList<NodeTypeMetadata> GetAllNodeTypes() => Types;

        public IReadOnlyList<NodeTypeMetadata> GetNodeTypes(Type nodeInterfaceType)
            => Types.Where(t => nodeInterfaceType.IsAssignableFrom(t.Type)).ToList();
    }

    /// <summary>对话界面注册表桩。</summary>
    internal sealed class StubChatViewRegistry : IChatViewRegistry
    {
        public List<ChatViewInfo> Views { get; } = new();

        public IReadOnlyList<ChatViewInfo> GetAll() => Views;

        public ChatViewInfo? GetById(string? id)
            => string.IsNullOrWhiteSpace(id) ? null : Views.FirstOrDefault(v => v.Id == id);

        public ChatViewInfo? GetDefault()
            => Views.FirstOrDefault(v => v.IsDefault) ?? Views.FirstOrDefault();
    }

    /// <summary>构建一个最小可用的 PersistentEnv；services 可为 null（模拟无宿主服务的上下文）。</summary>
    internal static PersistentEnv Env(
        IKVDataService? kv = null,
        IServiceProvider? services = null,
        Agent? agent = null,
        IToolRegistry? toolRegistry = null)
    {
        return new PersistentEnv
        {
            KVData = kv ?? new FakeKVDataService(),
            ToolRegistry = toolRegistry ?? Mock.Of<IToolRegistry>(r => r.AllTools == Array.Empty<ToolMetadata>()),
            Serializer = Mock.Of<IPreGenerationNodeSerializer>(),
            LocService = new KeyLoc(),
            DebugOutput = Mock.Of<IDebugOutputService>(),
            Chat = new Chat { Name = "TestChat" },
            Agent = agent ?? Agent.Create("LiveAgent", ""),
            Services = services
        };
    }

    /// <summary>单次工具调用的入参。</summary>
    internal static ToolInvocationContext Invocation(string toolName, string json) => new()
    {
        ToolCallId = "call-1",
        ToolName = toolName,
        ArgumentsJson = json,
        RoundIndex = 0
    };
}
