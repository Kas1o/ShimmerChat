using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

public class ToolCallLoopTests
{
    private readonly ToolCallLoop _loop = new();
    private static readonly IReadOnlyList<Tool> EmptyTools = Array.Empty<Tool>();

    [Fact]
    public async Task RunAsync_NoToolCall_Completes()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Hello"));
        var host = new StubToolCallLoopHost();

        await _loop.RunAsync(api, EmptyTools, host);

        api.CallCount.Should().Be(1);
        host.RoundCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_SingleToolCall_ExecutesAndCompletes()
    {
        var api = new ConfigurableChatClient();
        // Round 1: LLM returns a tool call
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "test_tool", id = "1", arguments = "{}", index = 0 }));
        // Round 2: LLM returns a normal response
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Done"));

        var toolExecuted = false;
        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) =>
            {
                toolExecuted = true;
                tc.name.Should().Be("test_tool");
                return Task.FromResult("result");
            });

        await _loop.RunAsync(api, EmptyTools, host);

        api.CallCount.Should().Be(2);
        toolExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MultipleToolCallRounds()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "tool_a", id = "1", arguments = "{}", index = 0 }));
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "tool_b", id = "2", arguments = "{}", index = 0 }));
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Final"));

        var executedTools = new List<string>();
        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) =>
            {
                executedTools.Add(tc.name);
                return Task.FromResult("ok");
            });

        await _loop.RunAsync(api, EmptyTools, host);

        executedTools.Should().Equal("tool_a", "tool_b");
        api.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_MultipleToolsSameRound_AllExecuted()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "tool_1", id = "1", arguments = "{}", index = 0 },
            new ToolCall { name = "tool_2", id = "2", arguments = "{}", index = 1 }));
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("Done"));

        var executed = new List<string>();
        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) =>
            {
                executed.Add(tc.name);
                return Task.FromResult("ok");
            });

        await _loop.RunAsync(api, EmptyTools, host);

        executed.Should().Equal("tool_1", "tool_2");
    }

    [Fact]
    public async Task RunAsync_MaxRoundsExhausted()
    {
        var api = new ConfigurableChatClient();
        // Always return FunctionCall — will loop until maxRounds
        for (int i = 0; i < 10; i++)
            api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
                new ToolCall { name = "loop_tool", id = "x", arguments = "{}", index = 0 }));

        var callCount = 0;
        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) => { callCount++; return Task.FromResult("ok"); });

        await _loop.RunAsync(api, EmptyTools, host, maxRounds: 3);

        // Should stop after 3 rounds (3 API calls, 3 tool executions)
        api.CallCount.Should().Be(3);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_HostReturnsFalse_Stops()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "t", id = "1", arguments = "{}", index = 0 }));

        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) => Task.FromResult("ok"),
            onComplete: (rsp, ct) => Task.FromResult(false)); // stop after first round

        await _loop.RunAsync(api, EmptyTools, host);

        // Host returned false, loop stopped before executing tools
        api.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_StreamAccumulation_CorrectAccumulation()
    {
        var api = new ConfigurableChatClient();
        // Use single round with multiple chunks; Stop means loop exits after round 1
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
    public async Task RunAsync_ToolError_ContinueOnError()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "failing_tool", id = "1", arguments = "{}", index = 0 }));
        api.AddResponse(ConfigurableChatClient.CreateStopResponse("recovered"));

        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) => throw new InvalidOperationException("boom"));

        // Should not throw because continueOnToolError = true
        await _loop.RunAsync(api, EmptyTools, host, continueOnToolError: true);

        api.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ToolError_Propagates()
    {
        var api = new ConfigurableChatClient();
        api.AddResponse(ConfigurableChatClient.CreateToolCallResponse(
            new ToolCall { name = "failing_tool", id = "1", arguments = "{}", index = 0 }));

        var host = new StubToolCallLoopHost(
            executeTool: (tc, ct) => throw new InvalidOperationException("boom"));

        var act = async () => await _loop.RunAsync(api, EmptyTools, host, continueOnToolError: false);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
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
