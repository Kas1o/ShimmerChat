using Newtonsoft.Json;
using SharperLLM.FunctionCalling;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

/// <summary>
/// IToolV2 + IAutoCreateToolV2 测试桩
/// </summary>
public class StubAutoCreateToolV2 : IAutoCreateToolV2
{
    private readonly string _nameKey;
    public StubAutoCreateToolV2(string nameKey = "stub_tool") => _nameKey = nameKey;

    public static string NameKey => "stub_tool";
    public static string DescriptionKey => "stub_tool.desc";
    public static string[] CategoryKeys => [];

    public static IAutoCreateToolV2 Create(PersistentEnv env) => new StubAutoCreateToolV2();

    public Tool GetDefinition() => new() { name = _nameKey, description = "", parameters = new() };
    public Task<string> ExecuteAsync(string input) => Task.FromResult("ok");
}

public class ToolPresetNodeTests : NodeTestBase
{
    private StubToolRegistry CreateRegistry()
    {
        var stub = new StubToolRegistry();
        var stubTool = new StubAutoCreateToolV2();
        return stub;
    }

    private static string CreatePresetsJson(params string[] presetNames)
    {
        var presets = presetNames.Select((n, i) => new ToolPreset
        {
            Id = $"p{i}",
            Name = n,
            IsDefault = i == 0,
            EnabledToolTypeNames = new List<string> { $"tool.{n}" }
        }).ToList();
        return JsonConvert.SerializeObject(presets);
    }

    private PersistentEnv CreateEnvWithRegistry(StubToolRegistry registry)
    {
        return new PersistentEnv
        {
            KVData = KvMock.Object,
            ChatGuid = Guid.NewGuid(),
            AgentGuid = Guid.NewGuid(),
            ToolRegistry = registry,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
        };
    }

    [Fact]
    public async Task NoPresets_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__")).Returns((string?)null);
        var node = new ToolPresetNode();
        var env = new GenerationEnv(CreateEnvWithRegistry(new StubToolRegistry()));
        var result = await node.ExecuteAsync(new NodeExecutionContext(env));
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.PresetNotFound);
    }

    [Fact]
    public async Task EmptyPresetName_UsesDefaultPreset()
    {
        var stubTool = new StubAutoCreateToolV2("tool.default_tools");
        var registry = new StubToolRegistry()
            .Register("tool.default_tools", typeof(StubAutoCreateToolV2))
            .SetInstance(typeof(StubAutoCreateToolV2), stubTool);
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__"))
            .Returns(CreatePresetsJson("default_tools", "other_tools"));
        var node = new ToolPresetNode { PresetName = "" };
        var env = new GenerationEnv(CreateEnvWithRegistry(registry));
        var ctx = new NodeExecutionContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Tools.Should().HaveCount(1);
    }

    [Fact]
    public async Task NamedPreset_Found_InstantiatesTools()
    {
        var stubTool = new StubAutoCreateToolV2("tool.target");
        var registry = new StubToolRegistry()
            .Register("tool.target", typeof(StubAutoCreateToolV2))
            .SetInstance(typeof(StubAutoCreateToolV2), stubTool);
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__"))
            .Returns(CreatePresetsJson("a", "target", "c"));
        var node = new ToolPresetNode { PresetName = "target" };
        var env = new GenerationEnv(CreateEnvWithRegistry(registry));
        var ctx = new NodeExecutionContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Tools.Should().HaveCount(1);
    }

    [Fact]
    public async Task NamedPreset_NotFound_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__"))
            .Returns(CreatePresetsJson("a", "b"));
        var node = new ToolPresetNode { PresetName = "nonexistent" };
        var env = new GenerationEnv(CreateEnvWithRegistry(new StubToolRegistry()));
        var result = await node.ExecuteAsync(new NodeExecutionContext(env));
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.PresetNotFound);
    }

    [Fact]
    public async Task ToolTypeName_NotFound_SkipsGracefully()
    {
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__"))
            .Returns(CreatePresetsJson("default"));
        var node = new ToolPresetNode { PresetName = "" };
        var env = new GenerationEnv(CreateEnvWithRegistry(new StubToolRegistry()));
        var ctx = new NodeExecutionContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyEnabledTools_SuccessEmptyTools()
    {
        var presets = new List<ToolPreset>
        {
            new() { Id = "p0", Name = "empty", IsDefault = true, EnabledToolTypeNames = new() }
        };
        KvMock.Setup(k => k.Read("ToolPresets", "__presets__"))
            .Returns(JsonConvert.SerializeObject(presets));
        var node = new ToolPresetNode { PresetName = "" };
        var env = new GenerationEnv(CreateEnvWithRegistry(new StubToolRegistry()));
        var ctx = new NodeExecutionContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Tools.Should().BeEmpty();
    }
}
