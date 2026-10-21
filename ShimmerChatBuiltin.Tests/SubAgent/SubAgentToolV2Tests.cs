using Newtonsoft.Json.Linq;
using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatBuiltin.SubAgent.Sessions;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// subagent_call：session_id 校验、续聊、Guid 作用域开关与失败落盘。
/// 这些用例都在「树执行完、进入 API 检查」处返回（未配置 API），因此不需要真实 LLM。
/// </summary>
public class SubAgentToolV2Tests
{
    private readonly FakeKVDataService _kv = new();
    private readonly Mock<IPreGenerationNodeSerializer> _serializer = new();
    private readonly Mock<IToolRegistry> _toolRegistry = new();
    private readonly Mock<ILocService> _loc = new();
    private readonly Mock<IDebugOutputService> _debug = new();
    private readonly Guid _parentChat = Guid.NewGuid();
    private readonly Guid _parentAgent = Guid.NewGuid();

    /// <summary>探针树：记录子代理树实际看到的作用域身份。</summary>
    private sealed class GuidProbeNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Probe";

        public Guid? ChatGuid { get; private set; }
        public Guid? AgentGuid { get; private set; }
        public int ChatMessageCount { get; private set; }
        public int Executions { get; private set; }

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            ChatGuid = context.Env.Persistent.ChatGuid;
            AgentGuid = context.Env.Persistent.AgentGuid;
            ChatMessageCount = context.Env.Persistent.Chat.Messages.Count;
            Executions++;
            return Task.FromResult(NodeResult.SuccessResult());
        }
    }

    private (SubAgentToolV2 Tool, GuidProbeNode Probe, SubAgentConfig Config) CreateTool(
        bool sharedAgentGuid = true, bool sharedChatGuid = true, string name = "probe")
    {
        var probe = new GuidProbeNode();
        _serializer.Setup(s => s.Deserialize(It.IsAny<string>())).Returns(probe);

        var config = new SubAgentConfig { Name = name, UseSharedPreset = false, ModifierTreeJson = "probe" };
        var tool = new SubAgentToolV2(_kv, _toolRegistry.Object, _parentChat, _parentAgent,
            _serializer.Object, _loc.Object, _debug.Object);

        tool.AddSubAgent(config, new SubAgentToolOptions
        {
            SharedAgentGuid = sharedAgentGuid,
            SharedChatGuid = sharedChatGuid
        });

        return (tool, probe, config);
    }

    private static JObject Parse(string json) => JObject.Parse(json);

    private static string Args(string subagent, string task, string? sessionId = null,
        bool useChatIdAlias = false)
    {
        var args = new JObject
        {
            ["subagent"] = subagent,
            ["task"] = task
        };

        if (sessionId != null)
            args[useChatIdAlias ? "chat_id" : "session_id"] = sessionId;

        return args.ToString();
    }

    private static Guid SessionIdOf(JObject envelope)
    {
        var raw = envelope["session_id"]?.Value<string>();
        raw.Should().NotBeNullOrWhiteSpace();
        return Guid.Parse(raw!);
    }

    [Fact]
    public async Task MissingSubagent_ReturnsSubagentRequired()
    {
        var (tool, _, _) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync("{\"task\":\"t\"}"));

        envelope["ok"]!.Value<bool>().Should().BeFalse();
        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.SubagentRequired);
    }

    [Fact]
    public async Task UnregisteredSubagent_ReturnsNotRegistered()
    {
        var (tool, _, _) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync(Args("other", "t")));

        envelope["ok"]!.Value<bool>().Should().BeFalse();
        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.SubagentNotRegistered);
        envelope["session_id"].Should().BeNull();
    }

    [Fact]
    public async Task InvalidSessionId_ReturnsInvalidSessionId()
    {
        var (tool, probe, _) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "t", "not-a-guid")));

        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.InvalidSessionId);
        probe.Executions.Should().Be(0);
    }

    [Fact]
    public async Task UnknownSessionId_ReturnsSessionNotFound()
    {
        var (tool, probe, _) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "t", Guid.NewGuid().ToString())));

        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.SessionNotFound);
        probe.Executions.Should().Be(0);
    }

    [Fact]
    public async Task SessionOfAnotherConfig_ReturnsOwnerMismatch_AndLeavesSessionUntouched()
    {
        var (tool, probe, _) = CreateTool();

        // 建一条属于别的配置的会话
        var otherConfig = new SubAgentConfig { Name = "other" };
        var started = SubAgentSessionStore.StartNew(_kv, null, otherConfig, "t",
            SubAgentSessionMessages.FromPrompt(new ChatMessage { Content = "t" }, PromptBuilder.From.user),
            null, null);
        var sessionId = started.Scope!.Session.Id;
        started.Scope.Dispose();

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "t", sessionId.ToString())));

        envelope["ok"]!.Value<bool>().Should().BeFalse();
        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.SessionOwnerMismatch);
        probe.Executions.Should().Be(0);
        SubAgentSessionStore.Load(_kv, null, sessionId).Session!.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task NoApi_PersistsFailedSessionWithParentLineage()
    {
        var (tool, probe, config) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "do it")));

        probe.Executions.Should().Be(1);
        envelope["ok"]!.Value<bool>().Should().BeFalse();
        envelope["code"]!.Value<string>().Should().Be(SubAgentToolV2.ErrorCodes.NoApi);
        envelope["status"]!.Value<string>().Should().Be("failed");

        var sessionId = SessionIdOf(envelope);
        var session = SubAgentSessionStore.Load(_kv, null, sessionId).Session!;
        session.Status.Should().Be(SubAgentSessionStatus.Failed);
        session.ErrorCode.Should().Be(SubAgentToolV2.ErrorCodes.NoApi);
        session.ConfigGuid.Should().Be(config.Guid);
        session.ParentChatGuid.Should().Be(_parentChat);
        session.ParentAgentGuid.Should().Be(_parentAgent);
        session.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task DefaultOptions_InheritParentGuids()
    {
        var (tool, probe, _) = CreateTool();

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "do it")));
        var sessionId = SessionIdOf(envelope);

        probe.ChatGuid.Should().Be(_parentChat);
        probe.AgentGuid.Should().Be(_parentAgent);
        probe.ChatGuid.Should().NotBe(sessionId);
    }

    [Fact]
    public async Task IndependentGuids_UseSessionIdAndConfigGuid()
    {
        var (tool, probe, config) = CreateTool(sharedAgentGuid: false, sharedChatGuid: false);

        var envelope = Parse(await tool.ExecuteAsync(Args("probe", "do it")));
        var sessionId = SessionIdOf(envelope);

        probe.ChatGuid.Should().Be(sessionId);
        probe.AgentGuid.Should().Be(config.Guid);
    }

    [Fact]
    public async Task Continuation_LoadsHistoryAndAppendsTask()
    {
        var (tool, probe, _) = CreateTool();

        var first = Parse(await tool.ExecuteAsync(Args("probe", "first task")));
        var sessionId = SessionIdOf(first);
        probe.ChatMessageCount.Should().Be(1);

        var second = Parse(await tool.ExecuteAsync(Args("probe", "second task", sessionId.ToString())));

        SessionIdOf(second).Should().Be(sessionId);
        probe.Executions.Should().Be(2);
        // 续聊时树看到的种子消息 = 会话历史 + 本次 task
        probe.ChatMessageCount.Should().Be(2);

        var session = SubAgentSessionStore.Load(_kv, null, sessionId).Session!;
        session.Messages.Select(m => m.CurrentVersion!.Content)
            .Should().Equal(["first task", "second task"]);
        SubAgentSessionStore.List(_kv, null).Summaries.Should().ContainSingle();
    }

    [Fact]
    public async Task ChatIdAlias_ContinuesSameSession()
    {
        var (tool, _, _) = CreateTool();

        var first = Parse(await tool.ExecuteAsync(Args("probe", "first task")));
        var sessionId = SessionIdOf(first);

        var second = Parse(await tool.ExecuteAsync(
            Args("probe", "second task", sessionId.ToString(), useChatIdAlias: true)));

        SessionIdOf(second).Should().Be(sessionId);
        SubAgentSessionStore.Load(_kv, null, sessionId).Session!.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void GetDefinition_ExposesSessionIdAndTitle()
    {
        var (tool, _, _) = CreateTool();

        var definition = tool.GetDefinition();
        var optional = definition.parameters!
            .Where(p => !p.required)
            .Select(p => p.parameter.name)
            .ToList();

        definition.name.Should().Be("subagent_call");
        definition.description.Should().Contain("session_id");
        optional.Should().BeEquivalentTo(["session_id", "title"]);
    }
}
