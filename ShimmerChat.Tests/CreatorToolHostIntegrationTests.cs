using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ShimmerChat.Singletons;
using ShimmerChatBuiltin.Creator;
using ShimmerChatBuiltin.Fragments;
using ShimmerChatBuiltin.NodeBasic.PostGeneration;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// Creator 工具集 × 宿主真实组件的集成契约：
/// 工具文档给模型的树 JSON 格式（包装器 + "$type" 完整类型名）必须能被
/// 真实的三个管线序列化器校验并往返；未知类型必须被白名单拒绝且不落库；
/// 节点目录来自真实的 INodeTypeCatalog 扫描。防止工具与宿主行为漂移。
/// </summary>
public class CreatorToolHostIntegrationTests
{
    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _data = new();

        public string? Read(string spaceId, string key)
            => _data.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
            => _data[(spaceId, key)] = value;
    }

    private sealed class KeyLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN", "en-US"];

        public string this[string key] => key;

        public string Format(string key, params object[] args) => key;

        public string Format(string key, params (string name, object value)[] args) => key;

        public void SetCulture(string culture) { }
    }

    /// <summary>按类型映射的真实宿主组件容器，模拟宿主 DI 对工具的服务解析。</summary>
    private sealed class HostServices : IServiceProvider
    {
        private readonly Dictionary<Type, object> _map = new();

        public HostServices Add<T>(T instance)
        {
            _map[typeof(T)] = instance!;
            return this;
        }

        public object? GetService(Type serviceType)
            => _map.TryGetValue(serviceType, out var value) ? value : null;
    }

    private static IPluginLoaderService LoaderWithBuiltinNodes()
    {
        var loader = new Mock<IPluginLoaderService>();
        loader.Setup(l => l.GetImplementingTypes(typeof(IPreGenerationNode)))
            .Returns(new List<Type> { typeof(SequenceNode), typeof(FragmentNode) });
        loader.Setup(l => l.GetImplementingTypes(typeof(IPostGenerationNode)))
            .Returns(new List<Type> { typeof(PostSequenceNode) });
        loader.Setup(l => l.GetImplementingTypes(typeof(IRenderModifierNode)))
            .Returns(new List<Type> { typeof(RenderSequenceNode) });
        return loader.Object;
    }

    private static (CreatorAgentTool AgentTool, CreatorNodeCatalogTool CatalogTool, PreGenerationNodeSerializer Pre) RealTools(
        MemoryKV kv, out Agent liveAgent)
    {
        var loader = LoaderWithBuiltinNodes();

        var preSerializer = new PreGenerationNodeSerializer(
            loader, NullLogger<PreGenerationNodeSerializer>.Instance);
        var postSerializer = new PostGenerationNodeSerializerService(
            loader, NullLogger<PostGenerationNodeSerializerService>.Instance);
        var renderSerializer = new RenderModifierNodeSerializer(
            loader, NullLogger<RenderModifierNodeSerializer>.Instance);
        var catalog = new NodeTypeCatalog(loader, NullLogger<NodeTypeCatalog>.Instance);

        var services = new HostServices()
            .Add<IPreGenerationNodeSerializer>(preSerializer)
            .Add<IPostGenerationNodeSerializerService>(postSerializer)
            .Add<IRenderModifierNodeSerializer>(renderSerializer)
            .Add<INodeTypeCatalog>(catalog);

        liveAgent = Agent.Create("LiveAgent", "");
        liveAgent.Save(kv);

        var env = new PersistentEnv
        {
            KVData = kv,
            ToolRegistry = Mock.Of<IToolRegistry>(r => r.AllTools == Array.Empty<ToolMetadata>()),
            Serializer = preSerializer,
            LocService = new KeyLoc(),
            DebugOutput = Mock.Of<IDebugOutputService>(),
            Chat = new Chat { Name = "TestChat" },
            Agent = liveAgent,
            Services = services
        };

        return (new CreatorAgentTool(env), new CreatorNodeCatalogTool(env), preSerializer);
    }

    private static ToolInvocationContext Invocation(string toolName, string json) => new()
    {
        ToolCallId = "call-1",
        ToolName = toolName,
        ArgumentsJson = json,
        RoundIndex = 0
    };

    // ------------------------------------------------------------------
    // 管线树写入 × 真实序列化器
    // ------------------------------------------------------------------

    /// <summary>
    /// 工具描述里给模型的树 JSON 形状（包装器字段 + "$type" 完整类型名 + 枚举数字值）
    /// 必须被真实前生成序列化器接受，且规范化后仍能按同一格式再次读取。
    /// </summary>
    [Fact]
    public async Task SetPipeline_AuthorableTreeJson_PassesRealValidationAndRoundTrips()
    {
        var kv = new MemoryKV();
        var (tool, _, pre) = RealTools(kv, out var agent);

        var authored = $$"""
        {
          "Id": "root-1",
          "Name": "Pre Root",
          "IsExpanded": true,
          "Node": {
            "$type": "{{typeof(SequenceNode).FullName}}",
            "Nodes": [
              {
                "Name": "System Prompt",
                "Node": {
                  "$type": "{{typeof(FragmentNode).FullName}}",
                  "Content": "You are a helpful assistant.",
                  "From": 2
                }
              }
            ]
          }
        }
        """;

        var result = await tool.ExecuteAsync(
            Invocation("creator_agent",
                $$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":{{Newtonsoft.Json.JsonConvert.SerializeObject(authored)}}}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed,
            "the documented tree shape must be accepted by the real serializer; got: {0}",
            result.ErrorMessage ?? result.Content);
        result.Content.Should().Contain("\"status\":\"updated\"");

        var stored = Agent.Load(agent.Guid, kv).PreGenerationTreeJson;
        stored.Should().NotBeNullOrWhiteSpace();

        // 规范化后的存储内容仍然按同一格式可解析（模型可以 get → edit → set 循环）。
        var reparsed = pre.DeserializeTree(stored!);
        reparsed.Success.Should().BeTrue();
        reparsed.Migrated.Should().BeFalse();

        var root = (PreGenerationNodeWrapper)reparsed.Root!;
        root.Name.Should().Be("Pre Root");
        var sequence = (SequenceNode)root.Node!;
        sequence.Nodes.Should().ContainSingle();

        var fragmentWrapper = sequence.Nodes[0];
        fragmentWrapper.Name.Should().Be("System Prompt");
        var fragment = (FragmentNode)fragmentWrapper.Node!;
        fragment.Content.Should().Be("You are a helpful assistant.");
        fragment.From.Should().Be(SharperLLM.Util.PromptBuilder.From.system,
            "enum values documented as numbers must map back to the right enum member");

        // 存储内容保留 "$type" 完整类型名，模型再次编辑时无需额外提示。
        stored.Should().Contain($"\"$type\": \"{typeof(FragmentNode).FullName}\"");
    }

    [Fact]
    public async Task SetPipeline_UnknownNodeType_IsRejectedByTheWhitelist()
    {
        var kv = new MemoryKV();
        var (tool, _, _) = RealTools(kv, out var agent);

        var authored = $$"""
        {
          "Name": "Bad Root",
          "Node": {
            "$type": "Bogus.Namespace.MissingNode",
            "Anything": 1
          }
        }
        """;

        var result = await tool.ExecuteAsync(
            Invocation("creator_agent",
                $$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":{{Newtonsoft.Json.JsonConvert.SerializeObject(authored)}}}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TreeJsonInvalid);
        result.ErrorMessage.Should().Contain("Bogus.Namespace.MissingNode");

        // 拒绝时不得留下任何写入。
        Agent.Load(agent.Guid, kv).PreGenerationTreeJson.Should().BeNull();
    }

    [Fact]
    public async Task SetPipeline_PostAndRender_ValidateWithTheirRealSerializers()
    {
        var kv = new MemoryKV();
        var (tool, _, _) = RealTools(kv, out var agent);

        var postTree = $$"""
        {
          "Name": "Post Root",
          "Node": {
            "$type": "{{typeof(PostSequenceNode).FullName}}",
            "Children": []
          }
        }
        """;
        var renderTree = $$"""
        {
          "Name": "Render Root",
          "Node": {
            "$type": "{{typeof(RenderSequenceNode).FullName}}",
            "Children": []
          }
        }
        """;

        var postResult = await tool.ExecuteAsync(
            Invocation("creator_agent",
                $$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"post","tree_json":{{Newtonsoft.Json.JsonConvert.SerializeObject(postTree)}}}"""),
            CancellationToken.None);
        var renderResult = await tool.ExecuteAsync(
            Invocation("creator_agent",
                $$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"render","tree_json":{{Newtonsoft.Json.JsonConvert.SerializeObject(renderTree)}}}"""),
            CancellationToken.None);

        postResult.Status.Should().Be(ToolResultStatus.Completed,
            "post serializer interface resolution must work; got: {0}", postResult.ErrorMessage ?? "");
        renderResult.Status.Should().Be(ToolResultStatus.Completed,
            "render serializer interface resolution must work; got: {0}", renderResult.ErrorMessage ?? "");

        var stored = Agent.Load(agent.Guid, kv);
        stored.PostGenerationTreeJson.Should().Contain(typeof(PostSequenceNode).FullName!);
        stored.RenderModifierTreeJson.Should().Contain(typeof(RenderSequenceNode).FullName!);
    }

    // ------------------------------------------------------------------
    // 节点目录 × 真实扫描
    // ------------------------------------------------------------------

    [Fact]
    public async Task NodeCatalog_ListWithRealCatalog_ExposesBuiltinNodeContract()
    {
        var kv = new MemoryKV();
        var (_, catalogTool, _) = RealTools(kv, out _);

        var result = await catalogTool.ExecuteAsync(
            Invocation("creator_node_catalog", """{"action":"list","pipeline":"pre"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);

        var sequence = payload["nodes"]!.Single(
            n => n["type"]!.Value<string>() == typeof(SequenceNode).FullName);
        sequence["pipelines"]!.Select(t => t.Value<string>()).Should().Equal("pre");
        sequence["properties"]!.Single(p => p["name"]!.Value<string>() == "Nodes")
            ["element_wrapper"]!.Value<string>().Should().Be("PreGenerationNodeWrapper");

        var fragment = payload["nodes"]!.Single(
            n => n["type"]!.Value<string>() == typeof(FragmentNode).FullName);
        fragment["properties"]!.Should().Contain(p => p["kind"]!.Value<string>() == "enum");
    }
}
