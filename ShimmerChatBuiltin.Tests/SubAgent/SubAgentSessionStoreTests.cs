using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatBuiltin.SubAgent.Sessions;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// 会话存储：索引维护、追加、状态落盘、删除，以及坏数据的严格报错。
/// </summary>
public class SubAgentSessionStoreTests
{
    private readonly FakeKVDataService _kv = new();

    private static SubAgentConfig Config(string name = "probe") => new() { Name = name };

    private static Message UserMessage(string text)
        => SubAgentSessionMessages.FromPrompt(new ChatMessage { Content = text }, PromptBuilder.From.user);

    private SubAgentSessionRunScope StartScope(SubAgentConfig config, string task = "do it")
    {
        var started = SubAgentSessionStore.StartNew(
            _kv, null, config, SubAgentSession.BuildTitle(null, task), UserMessage(task),
            Guid.NewGuid(), Guid.NewGuid());

        started.Status.Should().Be(SubAgentSessionLoadStatus.Ok);
        started.Scope.Should().NotBeNull();
        return started.Scope!;
    }

    [Fact]
    public void StartNew_CreatesSessionAndIndexEntry()
    {
        var config = Config();
        using var scope = StartScope(config, "整理资料");

        var session = scope.Session;
        scope.PersistError.Should().BeNull();
        session.Messages.Should().HaveCount(1);
        session.Title.Should().Be("整理资料");
        session.ConfigGuid.Should().Be(config.Guid);
        session.Status.Should().Be(SubAgentSessionStatus.Running);

        var (summaries, error) = SubAgentSessionStore.List(_kv, null);
        error.Should().BeNull();
        summaries.Should().ContainSingle();
        summaries[0].Id.Should().Be(session.Id);
        summaries[0].ConfigName.Should().Be("probe");
        summaries[0].MessageCount.Should().Be(1);
        summaries[0].LastPreview.Should().Be("整理资料");

        SubAgentSessionStore.Load(_kv, null, session.Id).Status.Should().Be(SubAgentSessionLoadStatus.Ok);
    }

    [Fact]
    public void Append_PersistsMessageAndUpdatesIndex()
    {
        using var scope = StartScope(Config());
        var id = scope.Session.Id;

        scope.Append(SubAgentSessionMessages.FromPrompt(
            new ChatMessage { Content = "answer", id = "call-1" }, PromptBuilder.From.assistant));

        var read = SubAgentSessionStore.Load(_kv, null, id);
        read.Status.Should().Be(SubAgentSessionLoadStatus.Ok);
        read.Session!.Messages.Should().HaveCount(2);
        read.Session.Messages[1].sender.Should().Be(Sender.AI);
        read.Session.Messages[1].CurrentVersion!.Content.Should().Be("answer");
        read.Session.Messages[1].CurrentVersion!.id.Should().Be("call-1");

        SubAgentSessionStore.List(_kv, null).Summaries[0].MessageCount.Should().Be(2);
    }

    [Fact]
    public void Complete_PersistsOutputAndStatus()
    {
        using var scope = StartScope(Config());
        var id = scope.Session.Id;

        scope.Complete("final output").Should().BeTrue();

        var session = SubAgentSessionStore.Load(_kv, null, id).Session!;
        session.Status.Should().Be(SubAgentSessionStatus.Completed);
        session.OutputText.Should().Be("final output");
        session.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void Fail_PersistsErrorAndKeepsMessages()
    {
        using var scope = StartScope(Config());
        var id = scope.Session.Id;
        scope.Append(UserMessage("second task"));

        scope.Fail(SubAgentToolV2.ErrorCodes.NoApi, "no api configured").Should().BeTrue();

        var session = SubAgentSessionStore.Load(_kv, null, id).Session!;
        session.Status.Should().Be(SubAgentSessionStatus.Failed);
        session.ErrorCode.Should().Be(SubAgentToolV2.ErrorCodes.NoApi);
        session.ErrorMessage.Should().Be("no api configured");
        session.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void Delete_RemovesSessionAndIndexEntry()
    {
        Guid id;
        using (var scope = StartScope(Config()))
        {
            id = scope.Session.Id;
        }

        SubAgentSessionStore.Delete(_kv, null, id).Should().BeNull();

        SubAgentSessionStore.Load(_kv, null, id).Status.Should().Be(SubAgentSessionLoadStatus.NotFound);
        SubAgentSessionStore.List(_kv, null).Summaries.Should().BeEmpty();
        _kv.Read(SubAgentSessionStore.SpaceId, SubAgentSessionStore.SessionKey(id)).Should().Be("");
    }

    [Fact]
    public void DeleteAllForConfig_OnlyRemovesMatchingConfig()
    {
        var configA = Config("a");
        var configB = Config("b");

        Guid idA, idB;
        using (var scope = StartScope(configA)) idA = scope.Session.Id;
        using (var scope = StartScope(configB)) idB = scope.Session.Id;

        var (deleted, error) = SubAgentSessionStore.DeleteAllForConfig(_kv, null, configA.Guid);

        error.Should().BeNull();
        deleted.Should().Be(1);
        SubAgentSessionStore.Load(_kv, null, idA).Status.Should().Be(SubAgentSessionLoadStatus.NotFound);
        SubAgentSessionStore.Load(_kv, null, idB).Status.Should().Be(SubAgentSessionLoadStatus.Ok);
    }

    [Fact]
    public void CorruptedIndex_IsReportedAndNeverOverwritten()
    {
        _kv.Write(SubAgentSessionStore.SpaceId, SubAgentSessionStore.IndexKey, "{ broken");

        var (summaries, error) = SubAgentSessionStore.List(_kv, null);
        summaries.Should().BeEmpty();
        error.Should().Contain("corrupted");

        // 索引坏掉时不得静默重建覆盖：落盘失败要暴露给调用方
        using var scope = StartScope(Config());
        scope.PersistError.Should().NotBeNull();
        _kv.Read(SubAgentSessionStore.SpaceId, SubAgentSessionStore.IndexKey).Should().Be("{ broken");
    }

    [Fact]
    public void CorruptedSession_IsReportedAsCorrupted()
    {
        var id = Guid.NewGuid();
        _kv.Write(SubAgentSessionStore.SpaceId, SubAgentSessionStore.SessionKey(id), "{ broken");

        var read = SubAgentSessionStore.Load(_kv, null, id);
        read.Status.Should().Be(SubAgentSessionLoadStatus.Corrupted);
        read.Error.Should().Contain("json parse failed");

        var opened = SubAgentSessionStore.Open(_kv, null, id);
        opened.Status.Should().Be(SubAgentSessionLoadStatus.Corrupted);
        opened.Scope.Should().BeNull();
    }

    [Fact]
    public void Delete_WhileSessionIsRunning_FailsFastWithoutBlocking()
    {
        var scope = StartScope(Config());
        var id = scope.Session.Id;

        // 会话被执行中的调用持有：删除必须立刻报错，不能阻塞调用方（界面）
        SubAgentSessionStore.Delete(_kv, null, id).Should().Be(SubAgentSessionStore.SessionInUseError);

        scope.Dispose();
        SubAgentSessionStore.Delete(_kv, null, id).Should().BeNull();
        SubAgentSessionStore.Load(_kv, null, id).Status.Should().Be(SubAgentSessionLoadStatus.NotFound);
    }

    [Fact]
    public async Task ConcurrentRuns_OnSameSession_DoNotLoseMessages()
    {
        Guid id;
        using (var scope = StartScope(Config()))
        {
            id = scope.Session.Id;
        }

        Task AppendAsync(string text) => Task.Run(() =>
        {
            var opened = SubAgentSessionStore.Open(_kv, null, id);
            opened.Status.Should().Be(SubAgentSessionLoadStatus.Ok);
            using var scope = opened.Scope!;
            scope.Append(UserMessage(text));
        });

        await Task.WhenAll(AppendAsync("one"), AppendAsync("two"));

        var session = SubAgentSessionStore.Load(_kv, null, id).Session!;
        session.Messages.Should().HaveCount(3);
        session.Messages.Select(m => m.CurrentVersion!.Content)
            .Should().BeEquivalentTo(["do it", "one", "two"]);
    }
}
