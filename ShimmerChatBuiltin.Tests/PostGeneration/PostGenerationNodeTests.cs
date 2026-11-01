using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatBuiltin.NodeBasic.PostGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.PostGeneration;

/// <summary>
/// 后处理节点的公共基建：可观测的对话、可替换的消息存储、可编排的工具。
/// 这些节点直接操作 Chat 与 MessageStore，所以断言也直接看这两者。
/// </summary>
public abstract class PostNodeTestBase
{
    protected readonly Mock<IKVDataService> KvMock = new();
    protected readonly Mock<IToolRegistry> ToolRegistryMock = new();
    protected readonly Mock<IPreGenerationNodeSerializer> SerializerMock = new();
    protected readonly Mock<ILocService> LocMock = new();
    protected readonly Mock<IDebugOutputService> DebugOutputMock = new();
    protected readonly Mock<IMessageStoreService> StoreMock = new();

    protected Chat TestChat { get; } = new() { Name = "TestChat" };

    protected PostNodeExecutionContext CreateContext(
        ChatMessage response,
        IReadOnlyList<IToolV2>? tools = null,
        bool withStore = true,
        Dictionary<string, object>? sharedState = null)
    {
        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
            Chat = TestChat,
            Agent = Agent.Create("TestAgent", ""),
            MessageStore = withStore ? StoreMock.Object : null
        };

        var env = new PostGenerationEnv(response, Array.Empty<ContextSegment>(), persistent,
            tools, sharedState: sharedState);

        return new PostNodeExecutionContext(env);
    }

    protected sealed class StubTool : IToolV2
    {
        private readonly string _name;
        private readonly Func<string, Task<string>> _execute;

        public StubTool(string name, Func<string, Task<string>>? execute = null)
        {
            _name = name;
            _execute = execute ?? (_ => Task.FromResult($"{name}-result"));
        }

        public Tool GetDefinition() => new() { name = _name, description = "stub" };

        public Task<string> ExecuteAsync(string input) => _execute(input);
    }

    protected static ToolCall Call(string name, string id = "call-1", string args = "{}")
        => new() { name = name, id = id, arguments = args };
}

public class PostToolCallNodeTests : PostNodeTestBase
{
    [Fact]
    public async Task WithToolCall_ExecutesAndContinues()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("echo")] },
            [new StubTool("echo", _ => Task.FromResult("echoed"))]);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);
        ctx.Env.ToolCallsHandled.Should().BeTrue();
        ctx.Env.ChatMutated.Should().BeTrue();

        TestChat.Messages.Should().ContainSingle();
        TestChat.Messages[0].sender.Should().Be(Sender.ToolResult);
        TestChat.Messages[0].CurrentVersion!.Content.Should().Be("echoed");
        TestChat.Messages[0].CurrentVersion!.id.Should().Be("call-1");

        StoreMock.Verify(s => s.InsertMessage(TestChat.Guid, It.IsAny<Message>()), Times.Once);
    }

    [Fact]
    public async Task MultipleToolCalls_EmitInOrder()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("a", "1"), Call("b", "2")] },
            [new StubTool("a"), new StubTool("b")]);

        await node.ExecuteAsync(ctx);

        TestChat.Messages.Select(m => m.CurrentVersion!.Content).Should().Equal("a-result", "b-result");
        TestChat.ValidateOrder().Should().BeNull();
    }

    [Fact]
    public async Task MaxCallsPerRound_DropsExtraCalls()
    {
        var node = new PostToolCallNode { MaxCallsPerRound = 1 };
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("a", "1"), Call("b", "2")] },
            [new StubTool("a"), new StubTool("b")]);

        await node.ExecuteAsync(ctx);

        TestChat.Messages.Should().ContainSingle();
        DebugOutputMock.Verify(d => d.Write("PostToolCallNode", "ToolCall", It.Is<string>(s => s.Contains("dropped"))),
            Times.Once);
    }

    [Fact]
    public async Task NoToolCall_LeavesDirectiveAtStop()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(new ChatMessage { Content = "plain" });

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Stop);
        ctx.Env.ToolCallsHandled.Should().BeFalse();
        TestChat.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task NoToolCall_CanForceContinue()
    {
        var node = new PostToolCallNode { NoToolCallBehavior = PostNoToolCallBehavior.Continue };
        var ctx = CreateContext(new ChatMessage { Content = "plain" });

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);
    }

    [Fact]
    public async Task MissingTool_ReportedToModel_WhenContinueOnError()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(new ChatMessage { Content = "", toolCalls = [Call("nope")] }, []);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        TestChat.Messages.Should().ContainSingle();
        TestChat.Messages[0].CurrentVersion!.Content.Should().Contain("not found");
        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);
    }

    [Fact]
    public async Task MissingTool_Fails_WhenNotContinueOnError()
    {
        var node = new PostToolCallNode { ContinueOnError = false };
        var ctx = CreateContext(new ChatMessage { Content = "", toolCalls = [Call("nope")] }, []);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ToolNotFound);
        TestChat.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolException_ReportedAndLogged_WhenContinueOnError()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("boom")] },
            [new StubTool("boom", _ => throw new InvalidOperationException("kaboom"))]);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        TestChat.Messages[0].CurrentVersion!.Content.Should().Contain("kaboom");
        DebugOutputMock.Verify(d => d.Write("PostToolCallNode", "ToolCall",
            It.Is<string>(s => s.Contains("kaboom"))), Times.Once);
    }

    [Fact]
    public async Task WithoutStore_OnlyMutatesMemory()
    {
        var node = new PostToolCallNode();
        var ctx = CreateContext(
            new ChatMessage { Content = "", toolCalls = [Call("echo")] },
            [new StubTool("echo")],
            withStore: false);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        TestChat.Messages.Should().ContainSingle();
        StoreMock.Verify(s => s.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>()), Times.Never);
    }
}

public class PostLoopDirectiveNodeTests : PostNodeTestBase
{
    [Fact]
    public async Task SetsDirective()
    {
        var node = new PostLoopDirectiveNode { Directive = PostLoopDirective.Continue };
        var ctx = CreateContext(new ChatMessage { Content = "x" });

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);
    }
}

public class PostMessageNodeTests : PostNodeTestBase
{
    [Fact]
    public async Task Append_AddsMessageAndPersists()
    {
        var node = new PostMessageNode { Mode = PostMessageMode.Append, Content = "remember this" };
        var ctx = CreateContext(new ChatMessage { Content = "x" });

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        TestChat.Messages.Should().ContainSingle();
        TestChat.Messages[0].sender.Should().Be(Sender.User);
        TestChat.Messages[0].CurrentVersion!.Content.Should().Be("remember this");
        StoreMock.Verify(s => s.InsertMessage(TestChat.Guid, It.IsAny<Message>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceResponse_OverwritesResponseText()
    {
        var node = new PostMessageNode { Mode = PostMessageMode.ReplaceResponse, Content = "rewritten" };
        var ctx = CreateContext(new ChatMessage { Content = "original" });

        await node.ExecuteAsync(ctx);

        ctx.Env.ResponseText.Should().Be("rewritten");
        TestChat.Messages.Should().BeEmpty();
    }
}

public class PostIfNodeTests : PostNodeTestBase
{
    [Fact]
    public async Task ResponseTextContains_RunsThen()
    {
        var ctx = CreateContext(new ChatMessage { Content = "final answer [[done]]" });
        var node = new PostIfNode
        {
            Source = PostConditionSource.ResponseText,
            Operator = PostConditionOperator.Contains,
            Value = "[[done]]",
            Then = new PostLoopDirectiveNode { Directive = PostLoopDirective.Stop }
        };
        ctx.Env.LoopDirective = PostLoopDirective.Continue;

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Stop);
    }

    [Fact]
    public async Task FirstToolName_MatchesThen()
    {
        var ctx = CreateContext(new ChatMessage { Content = "", toolCalls = [Call("finish")] });
        var node = new PostIfNode
        {
            Source = PostConditionSource.FirstToolName,
            Operator = PostConditionOperator.Is,
            Value = "finish",
            Then = new PostLoopDirectiveNode { Directive = PostLoopDirective.Stop }
        };
        ctx.Env.LoopDirective = PostLoopDirective.Continue;

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Stop);
    }

    [Fact]
    public async Task NoMatch_RunsElse()
    {
        var ctx = CreateContext(new ChatMessage { Content = "plain" });
        var node = new PostIfNode
        {
            Source = PostConditionSource.HasToolCall,
            Operator = PostConditionOperator.Is,
            Value = "true",
            Then = new PostLoopDirectiveNode { Directive = PostLoopDirective.Stop },
            Else = new PostLoopDirectiveNode { Directive = PostLoopDirective.Continue }
        };
        ctx.Env.LoopDirective = PostLoopDirective.Stop;

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Continue);
    }

    [Fact]
    public async Task SharedStateSource_ReadsCrossRoundValue()
    {
        var state = new Dictionary<string, object> { ["rounds"] = 3 };
        var ctx = CreateContext(new ChatMessage { Content = "x" }, sharedState: state);
        var node = new PostIfNode
        {
            Source = PostConditionSource.SharedState,
            SharedStateKey = "rounds",
            Operator = PostConditionOperator.Is,
            Value = "3",
            Then = new PostLoopDirectiveNode { Directive = PostLoopDirective.Stop }
        };
        ctx.Env.LoopDirective = PostLoopDirective.Continue;

        await node.ExecuteAsync(ctx);

        ctx.Env.LoopDirective.Should().Be(PostLoopDirective.Stop);
    }
}

public class PostChatTests : PostNodeTestBase
{
    [Fact]
    public void Append_AssignsSequentialOrderAndPersists()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" });

        PostChat.Append(ctx.Env, new ChatMessage { Content = "a" }, Sender.User);
        PostChat.Append(ctx.Env, new ChatMessage { Content = "b" }, Sender.User);

        TestChat.Messages.Select(m => m.Order).Should().Equal(0, 1);
        TestChat.ValidateOrder().Should().BeNull();
        StoreMock.Verify(s => s.InsertMessage(TestChat.Guid, It.IsAny<Message>()), Times.Exactly(2));
    }

    [Fact]
    public void InsertAfter_KeepsPositionAndReordersStore()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" });
        var first = PostChat.Append(ctx.Env, new ChatMessage { Content = "first" }, Sender.User);
        var third = PostChat.Append(ctx.Env, new ChatMessage { Content = "third" }, Sender.User);

        var middle = PostChat.InsertAfter(ctx.Env, first, new ChatMessage { Content = "middle" }, Sender.AI);

        TestChat.Messages.Select(m => m.CurrentVersion!.Content).Should().Equal("first", "middle", "third");
        TestChat.Messages.Select(m => m.Order).Should().Equal(0, 1, 2);
        middle.Order.Should().Be(1);
        third.Order.Should().Be(2);

        // 顺序由重排保证，不依赖 timestamp
        StoreMock.Verify(s => s.DeleteAllMessages(TestChat.Guid), Times.Once);
    }

    [Fact]
    public void InsertAfter_UnknownAnchor_Throws()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" });
        var orphan = new Message { sender = Sender.User, timestamp = DateTime.Now, message = new ChatMessage { Content = "o" } };

        var act = () => PostChat.InsertAfter(ctx.Env, orphan, new ChatMessage { Content = "x" }, Sender.User);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Remove_DeletesFromChatAndStore()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" });
        var msg = PostChat.Append(ctx.Env, new ChatMessage { Content = "gone" }, Sender.User);

        PostChat.Remove(ctx.Env, msg);

        TestChat.Messages.Should().BeEmpty();
        StoreMock.Verify(s => s.DeleteMessage(TestChat.Guid, msg), Times.Once);
    }

    [Fact]
    public void Replace_UpdatesContentAndPersists()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" });
        var msg = PostChat.Append(ctx.Env, new ChatMessage { Content = "before" }, Sender.User);
        // 已落库的消息走 UpdateMessage 而不是再次 InsertMessage
        StoreMock.Setup(s => s.GetMessage(TestChat.Guid, msg.Id)).Returns(msg);

        PostChat.Replace(ctx.Env, msg, new ChatMessage { Content = "after" });

        msg.CurrentVersion!.Content.Should().Be("after");
        StoreMock.Verify(s => s.UpdateMessage(TestChat.Guid, msg), Times.Once);
    }

    [Fact]
    public void WithoutStore_MutatesMemoryOnly()
    {
        var ctx = CreateContext(new ChatMessage { Content = "x" }, withStore: false);

        var msg = PostChat.Append(ctx.Env, new ChatMessage { Content = "a" }, Sender.User);
        PostChat.Remove(ctx.Env, msg);

        TestChat.Messages.Should().BeEmpty();
        StoreMock.Verify(s => s.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>()), Times.Never);
        StoreMock.Verify(s => s.DeleteMessage(It.IsAny<Guid>(), It.IsAny<Message>()), Times.Never);
    }
}
