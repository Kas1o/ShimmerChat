using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.AskUser;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Generation.ToolUi;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.AskUser;

/// <summary>
/// ask_user 的边界行为：没有交互通道时必须立刻失败、参数非法必须给出错误码、
/// 正常路径把交互结果原样变成模型可读的 JSON。
/// </summary>
public class AskUserToolV2Tests
{
    private sealed class StubInteractions : IToolInteractionService
    {
        public ToolInteractionRequest? LastRequest { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public Func<ToolInteractionResult> Result { get; set; } =
            () => new ToolInteractionResult
            {
                Outcome = ToolInteractionOutcome.Answered,
                Answer = new JObject { ["choice"] = "B" }
            };

        public Task<ToolInteractionResult> RequestAsync(
            Guid chatGuid, ToolInteractionRequest request, TimeSpan? timeout, CancellationToken ct)
        {
            LastRequest = request;
            LastTimeout = timeout;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result());
        }

        public IReadOnlyList<ToolInteractionPending> GetPending(Guid chatGuid)
            => Array.Empty<ToolInteractionPending>();

        public ToolInteractionSubmitOutcome Submit(Guid id, JObject? answer)
            => ToolInteractionSubmitOutcome.Ok();

        public ToolInteractionSubmitOutcome Skip(Guid id, string reason)
            => ToolInteractionSubmitOutcome.Ok();

        public int CancelAll(Guid chatGuid, string reason) => 0;

        public event Action<Guid>? PendingChanged { add { } remove { } }
    }

    private static PersistentEnv Env(IToolInteractionService? interactions)
    {
        var chat = new Chat { Name = "TestChat" };

        return new PersistentEnv
        {
            KVData = Mock.Of<IKVDataService>(),
            ToolRegistry = Mock.Of<IToolRegistry>(),
            Serializer = Mock.Of<IPreGenerationNodeSerializer>(),
            LocService = Mock.Of<ILocService>(),
            DebugOutput = Mock.Of<IDebugOutputService>(),
            Chat = chat,
            Agent = Agent.Create("TestAgent", ""),
            Interactions = interactions
        };
    }

    private static ToolInvocationContext Invocation(string json) => new()
    {
        ToolCallId = "call-1",
        ToolName = "ask_user",
        ArgumentsJson = json,
        RoundIndex = 0
    };

    [Fact]
    public async Task WithoutInteractionChannel_FailsImmediately()
    {
        var tool = new AskUserToolV2(Env(null));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Continue?"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InteractionUnavailable);
    }

    [Fact]
    public async Task MissingQuestion_IsReportedAsInvalidArguments()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(Invocation("""{"question":"   "}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        interactions.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task InvalidJson_IsReportedAsInvalidArguments()
    {
        var tool = new AskUserToolV2(Env(new StubInteractions()));

        var result = await tool.ExecuteAsync(Invocation("not json"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task AnsweredOutcome_BecomesModelReadableJson()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Which one?","options":["A","B"]}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("answered");
        result.Content.Should().Contain("\"B\"");

        var form = interactions.LastRequest!.Form!;
        form.Title.Should().Be("Which one?");
        form.Fields.Should().ContainSingle(f => f.Kind == ToolUiFieldKind.Choice)
            .Which.Options.Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public async Task OptionString_IsSplitIntoChoices()
    {
        var interactions = new StubInteractions();
        interactions.Result = () => new ToolInteractionResult { Outcome = ToolInteractionOutcome.Skipped };
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Pick","options":"red\ngreen, blue"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("skipped");
        interactions.LastRequest!.Form!.Fields
            .Single(f => f.Kind == ToolUiFieldKind.Choice)
            .Options.Should().BeEquivalentTo(["red", "green", "blue"]);
    }

    [Fact]
    public async Task Cancellation_PropagatesInsteadOfReturningAnOutcome()
    {
        var interactions = new StubInteractions
        {
            Result = () => throw new OperationCanceledException()
        };
        var tool = new AskUserToolV2(Env(interactions));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tool.ExecuteAsync(Invocation("""{"question":"Continue?"}"""), CancellationToken.None));
    }

    [Fact]
    public async Task TimeoutArgument_IsForwardedToTheInteraction()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        await tool.ExecuteAsync(
            Invocation("""{"question":"Which one?","options":["A"],"timeout_seconds":30}"""),
            CancellationToken.None);

        interactions.LastTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task MissingOrZeroTimeout_MeansWaitIndefinitely()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        await tool.ExecuteAsync(Invocation("""{"question":"Q"}"""), CancellationToken.None);
        interactions.LastTimeout.Should().BeNull();

        await tool.ExecuteAsync(Invocation("""{"question":"Q","timeout_seconds":0}"""), CancellationToken.None);
        interactions.LastTimeout.Should().BeNull();
    }

    [Fact]
    public async Task NegativeTimeout_IsReportedAsInvalidArguments()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Q","timeout_seconds":-5}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        interactions.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task ExcessiveTimeout_IsRejectedInsteadOfSilentlyClamped()
    {
        var interactions = new StubInteractions();
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Q","timeout_seconds":86400}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("3600");
    }

    [Fact]
    public async Task TimedOutOutcome_TellsTheModelToContinueWithoutAnAnswer()
    {
        var interactions = new StubInteractions
        {
            Result = () => new ToolInteractionResult { Outcome = ToolInteractionOutcome.TimedOut }
        };
        var tool = new AskUserToolV2(Env(interactions));

        var result = await tool.ExecuteAsync(
            Invocation("""{"question":"Q","timeout_seconds":10}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("timed_out");
    }
}
