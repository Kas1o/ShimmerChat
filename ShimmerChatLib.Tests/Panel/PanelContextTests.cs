using ShimmerChatLib.Interface;
using ShimmerChatLib.Panel;

namespace ShimmerChatLib.Tests.Panel;

/// <summary><see cref="IPanelDraftStore"/> 的内存实现桩。</summary>
internal sealed class StubDraftStore : IPanelDraftStore
{
    private readonly Dictionary<string, string> _drafts = new(StringComparer.Ordinal);

    public int Count => _drafts.Count;

    public string this[string key]
    {
        get => _drafts.TryGetValue(key, out var value) ? value : "";
        set => _drafts[key] = value;
    }

    public string GetOrCreate(string key, Func<string> factory) =>
        _drafts.TryGetValue(key, out var value) ? value : (_drafts[key] = factory());

    public void Remove(string key) => _drafts.Remove(key);
}

public class ChatPanelContextTests
{
    private static ChatPanelContext CreateContext(
        Func<string, Task<bool>>? send = null,
        Func<string, bool, Task<bool>>? insert = null)
    {
        return new ChatPanelContext
        {
            Chat = new Chat { Name = "chat", Guid = Guid.NewGuid() },
            Agent = Agent.Create("agent", ""),
            MessageStore = null!,
            DraftStore = new StubDraftStore(),
            IsGenerating = () => false,
            RequestRefreshAsync = () => Task.CompletedTask,
            SendUserMessageAsync = send ?? (_ => Task.FromResult(true)),
            InsertIntoInputAsync = insert ?? ((_, _) => Task.FromResult(true)),
            RegisterEventHandler = _ => { }
        };
    }

    [Fact]
    public async Task SendAsync_ForwardsNonEmptyText()
    {
        string? captured = null;
        var context = CreateContext(send: text =>
        {
            captured = text;
            return Task.FromResult(true);
        });

        var sent = await context.SendAsync("hello");

        Assert.True(sent);
        Assert.Equal("hello", captured);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public async Task SendAsync_RejectsBlankTextWithoutCallingHost(string text)
    {
        var called = false;
        var context = CreateContext(send: _ =>
        {
            called = true;
            return Task.FromResult(true);
        });

        var sent = await context.SendAsync(text);

        Assert.False(sent);
        Assert.False(called);
    }

    [Fact]
    public async Task SendAsync_PropagatesHostRefusal()
    {
        var context = CreateContext(send: _ => Task.FromResult(false));

        Assert.False(await context.SendAsync("hello"));
    }

    [Fact]
    public void Draft_IsPersistedByHostStore()
    {
        var store = new StubDraftStore();
        var context = CreateContext();
        var withStore = new ChatPanelContext
        {
            Chat = context.Chat,
            Agent = context.Agent,
            MessageStore = null!,
            DraftStore = store,
            IsGenerating = () => false,
            RequestRefreshAsync = () => Task.CompletedTask,
            SendUserMessageAsync = _ => Task.FromResult(true),
            InsertIntoInputAsync = (_, _) => Task.FromResult(true),
            RegisterEventHandler = _ => { }
        };

        Assert.Equal("", withStore.Draft);

        withStore.Draft = "draft text";

        Assert.Equal("draft text", withStore.Draft);
        Assert.Equal("draft text", store[withStore.DraftKey]);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void DraftKey_IsScopedToChatAndAgent()
    {
        var a = CreateContext();
        var b = CreateContext();

        Assert.NotEqual(a.DraftKey, b.DraftKey);
        Assert.Contains(a.Chat.Guid.ToString("N"), a.DraftKey, StringComparison.Ordinal);
        Assert.Contains(a.Agent.Guid.ToString("N"), a.DraftKey, StringComparison.Ordinal);
    }
}

public class AgentPanelContextTests
{
    private static LiveAgentPanelContext CreateLiveContext(Agent? agent = null)
    {
        var target = agent ?? Agent.Create("agent", "");
        return new LiveAgentPanelContext
        {
            TargetGuid = target.Guid,
            Agent = target,
            MessageStore = null!,
            DraftStore = new StubDraftStore(),
            RequestRefreshAsync = () => Task.CompletedTask,
            RegisterEventHandler = _ => { }
        };
    }

    [Fact]
    public void LiveAgentContext_DraftKey_IsScopedToAgent()
    {
        var agent = Agent.Create("agent", "");
        var context = CreateLiveContext(agent);

        Assert.Equal($"agent:{agent.Guid:N}", context.DraftKey);
        Assert.Equal($"agent:{agent.Guid:N}", $"agent:{context.TargetGuid:N}");
    }

    [Fact]
    public void LiveAgentContext_Draft_IsPersistedByHostStore()
    {
        var store = new StubDraftStore();
        var agent = Agent.Create("agent", "");
        var context = new LiveAgentPanelContext
        {
            TargetGuid = agent.Guid,
            Agent = agent,
            MessageStore = null!,
            DraftStore = store,
            RequestRefreshAsync = () => Task.CompletedTask,
            RegisterEventHandler = _ => { }
        };

        context.Draft = "agent draft";

        Assert.Equal("agent draft", store[context.DraftKey]);
    }

    [Fact]
    public void LiveAgentContext_ExposesLiveAgentInstance()
    {
        var agent = Agent.Create("agent", "");
        var context = CreateLiveContext(agent);

        Assert.Same(agent, context.Agent);
        Assert.Equal(agent.Guid, context.TargetGuid);
    }

    [Fact]
    public void Contexts_AreDistinguishedByType_NotByNullableFields()
    {
        var context = CreateLiveContext();

        // 基类不暴露 Agent 实体：需要 Agent 的面板必须对 LiveAgentPanelContext 做类型判断。
        Assert.IsAssignableFrom<AgentPanelContext>(context);
        Assert.IsType<LiveAgentPanelContext>(context);
    }
}
