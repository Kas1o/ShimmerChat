using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatBuiltin.NodeBasic.PostGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Tests.PostGeneration;

/// <summary>
/// 结构化工具（IToolV3）在执行节点上的分派与错误/取消语义：
/// 预期失败是结果，意外是异常，用户取消绝不能被吞成"工具错误"。
/// </summary>
public class PostToolCallV3Tests : PostNodeTestBase
{
    private sealed class StubToolV3 : IToolV3
    {
        private readonly Func<ToolInvocationContext, CancellationToken, Task<ToolResult>> _execute;
        private readonly string _name;

        public StubToolV3(string name, Func<ToolInvocationContext, CancellationToken, Task<ToolResult>> execute)
        {
            _name = name;
            _execute = execute;
        }

        public ToolInvocationContext? LastContext { get; private set; }

        public Tool GetDefinition() => new() { name = _name, description = "stub-v3" };

        public Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            LastContext = context;
            return _execute(context, ct);
        }
    }

    [Fact]
    public async Task CompletedResult_IsWrittenAsToolResultAndLoopContinues()
    {
        var tool = new StubToolV3("ask_user", (_, _) =>
            Task.FromResult(ToolResult.Ok("""{"status":"answered"}""")));

        var node = new PostToolCallNode();
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("ask_user", "call-7", """{"question":"Q"}""")] },
            [tool]);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);

        var written = TestChat.Messages.Should().ContainSingle().Subject;
        written.sender.Should().Be(Sender.ToolResult);
        written.message.Content.Should().Be("""{"status":"answered"}""");
        written.message.id.Should().Be("call-7");

        // 参数与轮次被原样传给工具
        tool.LastContext!.ArgumentsJson.Should().Be("""{"question":"Q"}""");
        tool.LastContext.RoundIndex.Should().Be(0);
    }

    [Fact]
    public async Task FailedResult_StopsTheRoundWhenContinueOnErrorIsOff()
    {
        var tool = new StubToolV3("broken", (_, _) =>
            Task.FromResult(ToolResult.Fail(ToolErrorCodes.ServiceError, "provider down")));

        var node = new PostToolCallNode { ContinueOnError = false };
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("broken")] },
            [tool]);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(ToolErrorCodes.ServiceError);
        TestChat.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedResult_IsReportedToTheModelWhenContinueOnErrorIsOn()
    {
        var tool = new StubToolV3("broken", (_, _) =>
            Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments, "missing 'query'")));

        var node = new PostToolCallNode { ContinueOnError = true };
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("broken")] },
            [tool]);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);

        var written = TestChat.Messages.Should().ContainSingle().Subject;
        written.message.Content.Should().Contain("[Tool error]");
        written.message.Content.Should().Contain(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task UserCancellation_PropagatesAndWritesNothing()
    {
        using var cts = new CancellationTokenSource();

        var tool = new StubToolV3("slow", async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return ToolResult.Ok("never");
        });

        var node = new PostToolCallNode();
        var baseCtx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("slow")] },
            [tool]);

        var ctx = new PostNodeExecutionContext(baseCtx.Env, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.ExecuteAsync(ctx));
        TestChat.Messages.Should().BeEmpty();
    }
}
