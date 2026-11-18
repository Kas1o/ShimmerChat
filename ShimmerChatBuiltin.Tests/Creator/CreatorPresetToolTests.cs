using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.Creator;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Tests.SubAgent;

namespace ShimmerChatBuiltin.Tests.Creator;

/// <summary>
/// creator_preset 的行为契约：三条管线的预设 CRUD、树校验、
/// "__default__" 保护、损坏存储的显式失败，以及管线过滤。
/// </summary>
public class CreatorPresetToolTests
{
    private readonly FakeKVDataService _kv = new();

    private static ToolInvocationContext Invocation(string json)
        => CreatorTestDoubles.Invocation("creator_preset", json);

    private CreatorPresetTool Tool(IServiceProvider? services = null)
        => new(CreatorTestDoubles.Env(kv: _kv, services));

    private void SeedPresets<TPreset>(GenerationPipeline pipeline, params TPreset[] presets)
        where TPreset : class, IPipelinePreset
    {
        PipelinePresetStore.WriteAll(_kv, pipeline, presets.ToList());
    }

    // ------------------------------------------------------------------
    // 列出 / 查看
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_WithoutFilter_ReturnsAllThreePipelines()
    {
        SeedPresets(GenerationPipeline.PreGeneration,
            new PreGenerationPreset { Id = "pre-1", Name = "Pre One" });
        SeedPresets(GenerationPipeline.PostGeneration,
            new PostGenerationPreset { Id = "post-1", Name = "Post One", Pinned = true });
        SeedPresets(GenerationPipeline.RenderModifier,
            new RenderModifierPreset { Id = "render-1", Name = "Render One" });

        var result = await Tool().ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["pre"]!.Should().HaveCount(1);
        payload["post"]!.Should().HaveCount(1);
        payload["post"]![0]!["pinned"]!.Value<bool>().Should().BeTrue();
        payload["render"]!.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_WithPipelineFilter_ReturnsOnlyThatPipeline()
    {
        SeedPresets(GenerationPipeline.PreGeneration,
            new PreGenerationPreset { Id = "pre-1", Name = "Pre One" });
        SeedPresets(GenerationPipeline.PostGeneration,
            new PostGenerationPreset { Id = "post-1", Name = "Post One" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"list","pipeline":"post"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["post"]!.Should().HaveCount(1);
        payload.ContainsKey("pre").Should().BeFalse();
    }

    [Fact]
    public async Task List_CorruptStore_IsExplicitServiceFailure()
    {
        var (spaceId, storeKey) = PipelinePresetStore.GetLocation(GenerationPipeline.PostGeneration);
        _kv.Write(spaceId, storeKey, "{ broken");

        var result = await Tool().ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ServiceError);
    }

    [Fact]
    public async Task Get_ReturnsNamePinnedAndTree()
    {
        SeedPresets(GenerationPipeline.RenderModifier,
            new RenderModifierPreset { Id = "r-1", Name = "Render One", RootNodeJson = "{\"tree\":1}" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"get","pipeline":"render","preset_id":"r-1"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["name"]!.Value<string>().Should().Be("Render One");
        payload["root_node_json"]!.Value<string>().Should().Be("{\"tree\":1}");
    }

    [Fact]
    public async Task Get_UnknownPreset_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"get","pipeline":"pre","preset_id":"missing"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    // ------------------------------------------------------------------
    // 新建
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsNewIdAndAppearsInList()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","pipeline":"pre","name":"Fresh","pinned":true}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var presetId = JObject.Parse(result.Content!)["preset_id"]!.Value<string>()!;

        var presets = PipelinePresetStore.ReadAll<PreGenerationPreset>(_kv, GenerationPipeline.PreGeneration);
        presets.Should().ContainSingle(p => p.Id == presetId && p.Name == "Fresh" && p.Pinned);
    }

    [Fact]
    public async Task Create_WithoutName_FailsExplicitly()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","pipeline":"pre","root_node_json":"{}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task Create_WithoutPipeline_FailsExplicitly()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","name":"NoPipeline"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task Create_WrongTypeRootNodeJson_FailsExplicitly()
    {
        // 非字符串的 root_node_json 不能被静默当成空树 "{}" 入库。
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","pipeline":"pre","name":"Broken","root_node_json":7}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("root_node_json");
        PipelinePresetStore.ReadAll<PreGenerationPreset>(_kv, GenerationPipeline.PreGeneration)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithTree_ValidatesBeforeStoring()
    {
        var serializer = new CreatorTestDoubles.StubTreeSerializer
        {
            OnDeserializeTree = _ => NodeDeserializationResult.Failed("unknown node type")
        };
        var tool = Tool(new CreatorTestDoubles.FakeServices()
            .With<IPostGenerationNodeSerializerService>(serializer));

        var result = await tool.ExecuteAsync(
            Invocation("""{"action":"create","pipeline":"post","name":"Broken","root_node_json":"{\"x\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TreeJsonInvalid);

        // 校验失败时不允许留下半成品预设。
        PipelinePresetStore.ReadAll<PostGenerationPreset>(_kv, GenerationPipeline.PostGeneration)
            .Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // 修改
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_RenamesWithoutTouchingTheTree()
    {
        SeedPresets(GenerationPipeline.PreGeneration,
            new PreGenerationPreset { Id = "p-1", Name = "Old", RootNodeJson = "{\"tree\":1}" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"update","pipeline":"pre","preset_id":"p-1","name":"New"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var preset = PipelinePresetStore.ReadAll<PreGenerationPreset>(_kv, GenerationPipeline.PreGeneration)
            .Single(p => p.Id == "p-1");
        preset.Name.Should().Be("New");
        preset.RootNodeJson.Should().Be("{\"tree\":1}");
    }

    [Fact]
    public async Task Update_InvalidTree_FailsAndKeepsOldTree()
    {
        SeedPresets(GenerationPipeline.PostGeneration,
            new PostGenerationPreset { Id = "p-1", Name = "Old", RootNodeJson = "{\"old\":true}" });

        var serializer = new CreatorTestDoubles.StubTreeSerializer
        {
            OnDeserializeTree = _ => NodeDeserializationResult.Failed("bogus $type")
        };
        var tool = Tool(new CreatorTestDoubles.FakeServices()
            .With<IPostGenerationNodeSerializerService>(serializer));

        var result = await tool.ExecuteAsync(
            Invocation("""{"action":"update","pipeline":"post","preset_id":"p-1","root_node_json":"{\"bad\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TreeJsonInvalid);
        PipelinePresetStore.ReadAll<PostGenerationPreset>(_kv, GenerationPipeline.PostGeneration)
            .Single().RootNodeJson.Should().Be("{\"old\":true}");
    }

    [Fact]
    public async Task Update_UnknownPreset_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"update","pipeline":"pre","preset_id":"missing","name":"X"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    [Fact]
    public async Task Update_WithoutAnyField_FailsExplicitly()
    {
        SeedPresets(GenerationPipeline.PreGeneration,
            new PreGenerationPreset { Id = "p-1", Name = "Old" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"update","pipeline":"pre","preset_id":"p-1"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task Update_DefaultPreset_IsProtected()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"update","pipeline":"pre","preset_id":"__default__","name":"Hijacked"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ProtectedTarget);
    }

    // ------------------------------------------------------------------
    // 删除
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesPresetFromStore()
    {
        SeedPresets(GenerationPipeline.RenderModifier,
            new RenderModifierPreset { Id = "r-1", Name = "Doomed" },
            new RenderModifierPreset { Id = "r-2", Name = "Stays" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"delete","pipeline":"render","preset_id":"r-1"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var remaining = PipelinePresetStore.ReadAll<RenderModifierPreset>(_kv, GenerationPipeline.RenderModifier);
        remaining.Should().ContainSingle(p => p.Id == "r-2");
    }

    [Fact]
    public async Task Delete_DefaultPreset_IsProtected()
    {
        SeedPresets(GenerationPipeline.PreGeneration,
            new PreGenerationPreset { Id = "__default__", Name = "Default" });

        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"delete","pipeline":"pre","preset_id":"__default__"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ProtectedTarget);
    }

    [Fact]
    public async Task Delete_UnknownPreset_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"delete","pipeline":"pre","preset_id":"missing"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    // ------------------------------------------------------------------
    // 参数层
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvalidArgumentsJson_IsExplicitFailure()
    {
        var result = await Tool().ExecuteAsync(Invocation("not json"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task UnknownAction_IsExplicitFailure()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"explode","pipeline":"pre"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }
}
