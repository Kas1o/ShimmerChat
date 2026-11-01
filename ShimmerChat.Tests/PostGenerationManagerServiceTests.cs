using Microsoft.Extensions.Logging.Abstractions;
using SharperLLM.Util;
using ShimmerChat.Singletons;
using ShimmerChatBuiltin.NodeBasic.PostGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 后生成管线管理器契约：节点直接操作对话与存储，返回值只汇总响应消息与循环意图。
/// </summary>
public class PostGenerationManagerServiceTests
{
    private readonly Mock<IKVDataService> _kvMock = new();
    private readonly Mock<IMessageStoreService> _storeMock = new();
    private readonly PostGenerationNodeSerializerService _serializer;

    public PostGenerationManagerServiceTests()
    {
        var loader = new Mock<IPluginLoaderService>();
        loader
            .Setup(l => l.GetImplementingTypes(typeof(IPostGenerationNode)))
            .Returns(new List<Type>
            {
                typeof(PostSequenceNode),
                typeof(PostLoopDirectiveNode),
                typeof(PostMessageNode),
                typeof(PostToolCallNode)
            });

        _serializer = new PostGenerationNodeSerializerService(
            loader.Object, NullLogger<PostGenerationNodeSerializerService>.Instance);
    }

    private PostGenerationManagerService CreateService() =>
        new(_serializer, _kvMock.Object, NullLogger<PostGenerationManagerService>.Instance);

    private (Agent Agent, PersistentEnv Persistent) CreateRound(string treeJson, Chat chat)
    {
        var agent = Agent.Create("TestAgent", "");
        agent.PostGenerationTreeJson = treeJson;

        var persistent = new PersistentEnv
        {
            KVData = _kvMock.Object,
            ToolRegistry = new Mock<IToolRegistry>().Object,
            Serializer = new Mock<IPreGenerationNodeSerializer>().Object,
            LocService = new Mock<ILocService>().Object,
            DebugOutput = new Mock<IDebugOutputService>().Object,
            Chat = chat,
            Agent = agent,
            MessageStore = _storeMock.Object
        };

        return (agent, persistent);
    }

    [Fact]
    public async Task ResponseText_BecomesResponseMessageContent()
    {
        var chat = new Chat { Name = "c" };
        var tree = _serializer.Serialize(new PostMessageNode
        {
            Mode = PostMessageMode.ReplaceResponse,
            Content = "rewritten"
        });
        var (agent, persistent) = CreateRound(tree, chat);

        var result = await CreateService().ExecuteAsync(
            agent, new ChatMessage { Content = "original" },
            Array.Empty<ContextSegment>(), persistent);

        result.Success.Should().BeTrue();
        result.ResponseMessage.Content.Should().Be("rewritten");
        result.LoopDirective.Should().Be(PostLoopDirective.Stop);
    }

    [Fact]
    public async Task LoopDirective_IsPropagated()
    {
        var chat = new Chat { Name = "c" };
        var tree = _serializer.Serialize(new PostSequenceNode
        {
            Children =
            {
                new PostLoopDirectiveNode { Directive = PostLoopDirective.Continue }
            }
        });
        var (agent, persistent) = CreateRound(tree, chat);

        var result = await CreateService().ExecuteAsync(
            agent, new ChatMessage { Content = "x" },
            Array.Empty<ContextSegment>(), persistent);

        result.LoopDirective.Should().Be(PostLoopDirective.Continue);
    }

    [Fact]
    public async Task NodesWriteDirectlyToChatAndStore()
    {
        var chat = new Chat { Name = "c" };
        var tree = _serializer.Serialize(new PostMessageNode
        {
            Mode = PostMessageMode.Append,
            Sender = PostMessageSender.User,
            Content = "injected"
        });
        var (agent, persistent) = CreateRound(tree, chat);

        var result = await CreateService().ExecuteAsync(
            agent, new ChatMessage { Content = "x" },
            Array.Empty<ContextSegment>(), persistent);

        result.ChatMutated.Should().BeTrue();
        chat.Messages.Should().ContainSingle();
        chat.Messages[0].CurrentVersion!.Content.Should().Be("injected");
        _storeMock.Verify(s => s.InsertMessage(chat.Guid, It.IsAny<Message>()), Times.Once);
    }

    [Fact]
    public async Task ToolCallNode_RunsToolsAndRequestsNextRound()
    {
        var chat = new Chat { Name = "c" };
        var tree = _serializer.Serialize(new PostToolCallNode());
        var (agent, persistent) = CreateRound(tree, chat);

        var response = new ChatMessage
        {
            Content = "",
            toolCalls = [new SharperLLM.API.ToolCall { name = "echo", id = "call-1", arguments = "{}" }]
        };

        var result = await CreateService().ExecuteAsync(
            agent, response,
            Array.Empty<ContextSegment>(), persistent,
            new PostPipelineContext { Tools = [new StubTool("echo")] });

        result.Success.Should().BeTrue();
        result.LoopDirective.Should().Be(PostLoopDirective.Continue);
        result.ToolCallsHandled.Should().BeTrue();

        chat.Messages.Should().ContainSingle();
        chat.Messages[0].sender.Should().Be(Sender.ToolResult);
        chat.Messages[0].CurrentVersion!.Content.Should().Be("echoed");
        _storeMock.Verify(s => s.InsertMessage(chat.Guid, It.IsAny<Message>()), Times.Once);
    }

    [Fact]
    public async Task NodeFailure_StopsTheLoop()
    {
        var chat = new Chat { Name = "c" };
        // 空树 → 回退到 __default__ 预设；KVData 里没有预设 → 节点失败。
        // 失败必须停在当前轮，不能带着未处理完的状态继续循环。
        var (agent, persistent) = CreateRound("", chat);

        var result = await CreateService().ExecuteAsync(
            agent, new ChatMessage { Content = "x" },
            Array.Empty<ContextSegment>(), persistent,
            new PostPipelineContext { RoundIndex = 2 });

        result.Success.Should().BeFalse();
        result.LoopDirective.Should().Be(PostLoopDirective.Stop);
    }

    private sealed class StubTool : IToolV2
    {
        private readonly string _name;

        public StubTool(string name) => _name = name;

        public SharperLLM.FunctionCalling.Tool GetDefinition()
            => new() { name = _name, description = "stub" };

        public Task<string> ExecuteAsync(string input) => Task.FromResult($"{_name}ed");
    }
}
