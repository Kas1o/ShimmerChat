using SharperLLM.API;
using SharperLLM.FunctionCalling;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 轮次循环只负责"流式累积 + 轮次上限 + 询问宿主是否继续"。
/// 工具执行与业务判断已下沉到后处理管线，不在这一层测试。
/// </summary>
public class ToolCallLoopTests
{
    private readonly ToolCallLoop _loop = new();
    private static readonly IReadOnlyList<Tool> EmptyTools = Array.Empty<Tool>();

    [Fact]
    public async Task RunAsync_HostStops_CompletesSingleRound()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Hello"));
        var host = new StubToolCallLoopHost();

        await _loop.RunAsync(api, EmptyTools, host);

        api.CallCount.Should().Be(1);
        host.RoundCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HostContinues_RepeatsRounds()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "test_tool", id = "1", arguments = "{}", index = 0 }));
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Done"));

        var host = new StubToolCallLoopHost(
            onComplete: (rsp, ct) => Task.FromResult(rsp.Body.toolCalls is { Count: > 0 }));

        await _loop.RunAsync(api, EmptyTools, host);

        api.CallCount.Should().Be(2);
        host.RoundCount.Should().Be(2);
        host.Decisions.Should().Equal(true, false);
    }

    [Fact]
    public async Task RunAsync_MaxRoundsExhausted()
    {
        var api = new ConfigurableChatClient();
        for (int i = 0; i < 10; i++)
            api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
                new ToolCall { name = "loop_tool", id = "x", arguments = "{}", index = 0 }));

        var host = new StubToolCallLoopHost(onComplete: (_, _) => Task.FromResult(true));

        await _loop.RunAsync(api, EmptyTools, host, maxRounds: 3);

        api.CallCount.Should().Be(3);
        host.RoundCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_StreamAccumulation_CorrectAccumulation()
    {
        var api = new ConfigurableChatClient();
        api.AddStreamChunks(
            ConfigurableChatClient.CreateChunk("Hel"),
            ConfigurableChatClient.CreateChunk("lo "),
            ConfigurableChatClient.CreateChunk("World", FinishReason.Stop));

        var receivedContents = new List<string>();
        var host = new StubToolCallLoopHost(
            onDelta: (acc, ct) =>
            {
                receivedContents.Add(acc.Body.Content);
                return Task.CompletedTask;
            });

        await _loop.RunAsync(api, EmptyTools, host);

        receivedContents.Should().Contain("Hello ").And.Contain("Hello World");
    }

    [Fact]
    public async Task RunAsync_NonStreaming_UsesGenerateAsync()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Once"));

        var deltas = new List<string>();
        var host = new StubToolCallLoopHost(
            onDelta: (acc, ct) =>
            {
                deltas.Add(acc.Body.Content);
                return Task.CompletedTask;
            });

        await _loop.RunAsync(api, EmptyTools, host, streaming: false);

        api.CallCount.Should().Be(1);
        deltas.Should().Equal("Once");
        host.RoundCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HostReturnsFalse_StopsBeforeNextRound()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "t", id = "1", arguments = "{}", index = 0 }));

        var host = new StubToolCallLoopHost(onComplete: (_, _) => Task.FromResult(false));

        await _loop.RunAsync(api, EmptyTools, host);

        api.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_Cancellation_Throws()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("hello"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _loop.RunAsync(api, EmptyTools, new StubToolCallLoopHost(), ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
