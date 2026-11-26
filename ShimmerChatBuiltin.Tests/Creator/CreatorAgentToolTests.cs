using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.Creator;
using ShimmerChatLib;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Storage;
using ShimmerChatBuiltin.Tests.SubAgent;

namespace ShimmerChatBuiltin.Tests.Creator;

/// <summary>
/// creator_agent 的行为契约：
/// 成功路径（建/列/查/改/删/管线树读写）与失败路径（参数非法、目标不存在、
/// 校验失败、无宿主服务、删除受保护目标）都必须显式返回，不允许静默修补。
/// </summary>
public class CreatorAgentToolTests
{
    private readonly FakeKVDataService _kv = new();

    private static ToolInvocationContext Invocation(string json)
        => CreatorTestDoubles.Invocation("creator_agent", json);

    private CreatorAgentTool Tool(IServiceProvider? services = null, Agent? liveAgent = null)
        => new(CreatorTestDoubles.Env(kv: _kv, services, agent: liveAgent));

    // ------------------------------------------------------------------
    // 元数据 CRUD
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ThenList_ReturnsTheAgent()
    {
        var tool = Tool();

        var created = await tool.ExecuteAsync(
            Invocation("""{"action":"create","name":"Writer","user_intro":"A writing agent","tags":["wip"]}"""),
            CancellationToken.None);

        created.Status.Should().Be(ToolResultStatus.Completed);
        var guid = JObject.Parse(created.Content!)["agent_id"]!.Value<string>();
        guid.Should().NotBeNullOrEmpty();

        // 注册表里必须能找到新智能体，列表输出包含名称与标签。
        Agent.GetAllAgentGuids(_kv).Select(g => g.ToString()).Should().Contain(guid);

        var listed = await tool.ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);
        listed.Status.Should().Be(ToolResultStatus.Completed);
        listed.Content.Should().Contain("Writer").And.Contain("wip");

        var stored = Agent.Load(Guid.Parse(guid!), _kv);
        stored.UserIntro.Should().Be("A writing agent");
        stored.Tags.Should().BeEquivalentTo(["wip"]);
    }

    [Fact]
    public async Task Create_WithoutName_FailsExplicitly()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","user_intro":"no name"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task Create_WrongTypeTags_FailsExplicitly()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"create","name":"T","tags":"not-an-array"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("tags");
    }

    [Fact]
    public async Task Update_WrongTypeGreeting_FailsExplicitly()
    {
        // 数字冒充字符串不能被当成 null 处理——那会静默清空 greeting。
        var agent = Agent.Create("Greeter", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","greeting":123}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("greeting");
        Agent.Load(agent.Guid, _kv).Greeting.Should().BeNull();
    }

    [Fact]
    public async Task Get_UnknownAgent_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"get","agent_id":"00000000-0000-0000-0000-000000000001"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    [Fact]
    public async Task Update_OnlyTouchesFieldsPresentInArguments()
    {
        var agent = Agent.Create("Original", "Original intro");
        agent.Tags = ["keep"];
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","name":"Renamed"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("name");

        // 未提供的字段保持原值；name 已更新。
        var stored = Agent.Load(agent.Guid, _kv);
        stored.Name.Should().Be("Renamed");
        stored.UserIntro.Should().Be("Original intro");
        stored.Tags.Should().BeEquivalentTo(["keep"]);
    }

    [Fact]
    public async Task Update_ExplicitNullGreeting_ClearsIt()
    {
        var agent = Agent.Create("Greeter", "intro", greeting: "hello");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","greeting":null}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        Agent.Load(agent.Guid, _kv).Greeting.Should().BeNull();
    }

    [Fact]
    public async Task Update_UnknownAgent_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"update","agent_id":"00000000-0000-0000-0000-000000000002","name":"X"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    [Fact]
    public async Task Update_AvatarPath_FromUserImagesUrl_StoresRelativePath()
    {
        using var images = new TempUserUploadImage();
        var agent = Agent.Create("AvatarOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","avatar_path":"{{images.Url}}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("avatar_path");
        Agent.Load(agent.Guid, _kv).AvatarPath.Should().Be(images.FileName);
    }

    [Fact]
    public async Task Create_WithAvatarPath_StoresIt()
    {
        using var images = new TempUserUploadImage();

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"create","name":"WithAvatar","avatar_path":"{{images.FileName}}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var guid = JObject.Parse(result.Content!)["agent_id"]!.Value<string>();
        Agent.Load(Guid.Parse(guid!), _kv).AvatarPath.Should().Be(images.FileName);
    }

    [Fact]
    public async Task Update_AvatarPath_NonExistingFile_FailsAndKeepsOldPath()
    {
        using var images = new TempUserUploadImage();
        var agent = Agent.Create("AvatarOwner", "");
        agent.AvatarPath = "old.png";
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","avatar_path":"{{images.Url}}missing.png"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("avatar_path");
        Agent.Load(agent.Guid, _kv).AvatarPath.Should().Be("old.png");
    }

    [Fact]
    public async Task Update_AvatarPath_Traversal_IsRejected()
    {
        var agent = Agent.Create("AvatarOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","avatar_path":"../escape.png"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("UserUploadImage");
        Agent.Load(agent.Guid, _kv).AvatarPath.Should().BeNull();
    }

    [Fact]
    public async Task Update_ExplicitNullAvatarPath_ClearsIt()
    {
        var agent = Agent.Create("AvatarOwner", "");
        agent.AvatarPath = "old.png";
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","avatar_path":null}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        Agent.Load(agent.Guid, _kv).AvatarPath.Should().BeNull();
    }

    [Fact]
    public async Task Update_WrongTypeAvatarPath_FailsExplicitly()
    {
        var agent = Agent.Create("AvatarOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","avatar_path":123}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("avatar_path");
        Agent.Load(agent.Guid, _kv).AvatarPath.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithoutAnyField_FailsExplicitly()
    {
        var agent = Agent.Create("Untouched", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task Update_UnknownChatView_FailsWithKnownIds()
    {
        var agent = Agent.Create("Viewer", "");
        agent.Save(_kv);

        var registry = new CreatorTestDoubles.StubChatViewRegistry();
        registry.Views.Add(new ChatViewInfo("standard", "chatview.standard", null, "▤", 0, true, typeof(object)));
        var tool = Tool(new CreatorTestDoubles.FakeServices().With<IChatViewRegistry>(registry));

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"update","agent_id":"{{agent.Guid}}","chat_view_id":"nope"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("standard");
    }

    [Fact]
    public async Task Delete_RemovesAgentFromRegistry_KeepsChats()
    {
        var agent = Agent.Create("Doomed", "");
        agent.ChatGuids.Add(Guid.NewGuid());
        agent.Save(_kv);
        Agent.AddAgentToAll(agent, _kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"delete","agent_id":"{{agent.Guid}}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("detached_chat_count");
        Agent.GetAllAgentGuids(_kv).Should().NotContain(agent.Guid);
        // 与 UI 删除一致：记录本体与对话仍保留在存储里，只是不再出现在注册表列表中。
        var stored = Agent.Load(agent.Guid, _kv);
        stored.ChatGuids.Should().HaveCount(1);
    }

    [Fact]
    public async Task Delete_CurrentSessionAgent_IsProtected()
    {
        var live = Agent.Create("Self", "");
        live.Save(_kv);

        var tool = Tool(liveAgent: live);

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"delete","agent_id":"{{live.Guid}}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ProtectedTarget);
    }

    [Fact]
    public async Task Delete_UnknownAgent_IsTargetNotFound()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"delete","agent_id":"00000000-0000-0000-0000-000000000003"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TargetNotFound);
    }

    [Fact]
    public async Task List_UnreadableRegistryEntry_IsReportedNotHidden()
    {
        var orphan = Guid.NewGuid();
        _kv.Write("Agents", "__AllAgents__", JsonConvert.SerializeObject(new List<Guid> { orphan }));

        var result = await Tool().ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("unreadable").And.Contain(orphan.ToString());
    }

    // ------------------------------------------------------------------
    // 管线树
    // ------------------------------------------------------------------

    private CreatorTestDoubles.StubTreeSerializer ValidatingSerializer()
    {
        var serializer = new CreatorTestDoubles.StubTreeSerializer();
        serializer.OnDeserializeTree =
            json => NodeDeserializationResult.Ok(new PreGenerationNodeWrapper(new SequenceNode()));
        serializer.OnSerialize = _ => "{\"Id\":\"normalized-root\"}";
        return serializer;
    }

    [Fact]
    public async Task SetPipeline_ValidTree_IsStoredNormalized()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.Save(_kv);

        var tool = Tool(new CreatorTestDoubles.FakeServices()
            .With<IPreGenerationNodeSerializer>(ValidatingSerializer()));

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":"{\"anything\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("\"status\":\"updated\"");

        // 存储里写入的是序列化器规范化后的 JSON，而不是模型传来的原始文本。
        Agent.Load(agent.Guid, _kv).PreGenerationTreeJson
            .Should().Be("{\"Id\":\"normalized-root\"}");
    }

    [Fact]
    public async Task SetPipeline_InvalidTree_FailsAndKeepsOldTree()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.PreGenerationTreeJson = "{\"old\":true}";
        agent.Save(_kv);

        var serializer = new CreatorTestDoubles.StubTreeSerializer
        {
            OnDeserializeTree = _ => NodeDeserializationResult.Failed("unknown node type")
        };
        var tool = Tool(new CreatorTestDoubles.FakeServices()
            .With<IPreGenerationNodeSerializer>(serializer));

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":"{\"bad\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.TreeJsonInvalid);
        result.ErrorMessage.Should().Contain("unknown node type");

        // 失败路径不允许写坏数据：旧树保持不变。
        Agent.Load(agent.Guid, _kv).PreGenerationTreeJson.Should().Be("{\"old\":true}");
    }

    [Fact]
    public async Task SetPipeline_EmptyTree_ClearsThePrivateTree()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.PreGenerationTreeJson = "{\"old\":true}";
        agent.Save(_kv);

        var tool = Tool();

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":"{}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("\"status\":\"cleared\"");
        Agent.Load(agent.Guid, _kv).PreGenerationTreeJson.Should().BeNull();
    }

    [Fact]
    public async Task SetPipeline_MissingTreeJson_FailsExplicitly()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task SetPipeline_WithoutHostServices_IsExplicitFailure()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.Save(_kv);

        // 无宿主服务（如子代理虚拟会话）：必须显式失败，而不是跳过校验写坏数据。
        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"post","tree_json":"{\"x\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.HostServiceUnavailable);
    }

    [Fact]
    public async Task SetPipeline_OnCurrentSessionAgent_SyncsTheLiveInstance()
    {
        var live = Agent.Create("LiveTree", "");
        live.Save(_kv);

        var serializer = ValidatingSerializer();
        var tool = Tool(new CreatorTestDoubles.FakeServices()
            .With<IPreGenerationNodeSerializer>(serializer)
            .With<IRenderModifierNodeSerializer>(serializer), liveAgent: live);

        var result = await tool.ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{live.Guid}}","pipeline":"render","tree_json":"{\"x\":1}"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        // 写穿：宿主持有的活实例立即生效（下一轮生成使用新树）。
        live.RenderModifierTreeJson.Should().Be("{\"Id\":\"normalized-root\"}");
    }

    [Fact]
    public async Task SetPipeline_WrongTypeTreeJson_FailsExplicitly()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.PreGenerationTreeJson = "{\"old\":true}";
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"set_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre","tree_json":42}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("tree_json");
        // 类型错误不得被当成「清除」处理。
        Agent.Load(agent.Guid, _kv).PreGenerationTreeJson.Should().Be("{\"old\":true}");
    }

    [Fact]
    public async Task GetPipeline_ReturnsRawStoredJson()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.PreGenerationTreeJson = "{\"raw\":\"tree\"}";
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"get_pipeline","agent_id":"{{agent.Guid}}","pipeline":"pre"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("{\"raw\":\"tree\"}");
    }

    [Fact]
    public async Task GetPipeline_WithoutPrivateTree_ReportsDefaultApplies()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"get_pipeline","agent_id":"{{agent.Guid}}","pipeline":"post"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["tree_json"]!.Type.Should().Be(JTokenType.Null);
        payload["note"]!.Value<string>().Should().Contain("global/default");
    }

    [Fact]
    public async Task PipelineActions_WithInvalidPipelineValue_Fail()
    {
        var agent = Agent.Create("TreeOwner", "");
        agent.Save(_kv);

        var result = await Tool().ExecuteAsync(
            Invocation($$"""{"action":"get_pipeline","agent_id":"{{agent.Guid}}","pipeline":"bogus"}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("pre").And.Contain("post").And.Contain("render");
    }

    // ------------------------------------------------------------------
    // 对话界面
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListChatViews_ReturnsRegisteredViews()
    {
        var registry = new CreatorTestDoubles.StubChatViewRegistry();
        registry.Views.Add(new ChatViewInfo("standard", "chatview.standard", "chatview.standard.desc", "▤", 0, true, typeof(object)));
        registry.Views.Add(new ChatViewInfo("aggregated", "chatview.aggregated", null, "▥", 1, false, typeof(object)));

        var tool = Tool(new CreatorTestDoubles.FakeServices().With<IChatViewRegistry>(registry));

        var result = await tool.ExecuteAsync(
            Invocation("""{"action":"list_chat_views"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("standard").And.Contain("aggregated");
    }

    [Fact]
    public async Task ListChatViews_WithoutRegistry_FailsExplicitly()
    {
        var result = await Tool().ExecuteAsync(
            Invocation("""{"action":"list_chat_views"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.HostServiceUnavailable);
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
            Invocation("""{"action":"explode"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Tool().ExecuteAsync(Invocation("""{"action":"list"}"""), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// 临时切换 UserUploadImage 根目录并放一张真实文件；Creator 工具会校验文件存在，
    /// 测试结束后恢复原布局，避免污染真实 Data 目录。
    /// </summary>
    private sealed class TempUserUploadImage : IDisposable
    {
        private readonly DataPaths _previous;
        private readonly string _root;

        public TempUserUploadImage()
        {
            _previous = DataPaths.Current;
            _root = Path.Combine(Path.GetTempPath(), "shimmer-creator-image", Guid.NewGuid().ToString("N"));
            DataPaths.Configure(new DataPaths(_root));

            Directory.CreateDirectory(DataPaths.Current.UserUploadImageDirectory);
            FileName = $"{Guid.NewGuid():N}.png";
            File.WriteAllBytes(
                Path.Combine(DataPaths.Current.UserUploadImageDirectory, FileName),
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        }

        public string FileName { get; }

        public string Url => $"{AgentImagePath.RequestPath}/{FileName}";

        public void Dispose()
        {
            DataPaths.Configure(_previous);
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
    }
}
