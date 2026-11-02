using SharperLLM.Util;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class AppendChatMessagesNodeTests : NodeTestBase
{
    private static Message CreateMsg(string sender, string content, MessageGenerationState state = MessageGenerationState.Completed)
        => new() { sender = sender, timestamp = DateTime.UtcNow, message = new ChatMessage { Content = content }, GenerationState = state };

    [Fact]
    public async Task NoChatMessages_ReturnsSuccess()
    {
        var node = new AppendChatMessagesNode();
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UserMessage_MapsToFromUser()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.User, "hello"));
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.user);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("hello");
    }

    [Fact]
    public async Task AIMessage_MapsToFromAssistant()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.AI, "reply"));
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.assistant);
    }

    [Fact]
    public async Task RegeneratingMessage_Skipped()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.User, "skip", MessageGenerationState.Regenerating));
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.AI, "keep"));
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("keep");
    }

    [Fact]
    public async Task MultipleMessages_AllAppended()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.System, "s"));
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.User, "u"));
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.AI, "a"));
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.ToolResult, "t"));
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(4);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.system);
        ctx.Env.Transient.Fragments[1].From.Should().Be(PromptBuilder.From.user);
        ctx.Env.Transient.Fragments[2].From.Should().Be(PromptBuilder.From.assistant);
        ctx.Env.Transient.Fragments[3].From.Should().Be(PromptBuilder.From.tool_result);
    }

    [Fact]
    public async Task MessageWithImages_ResolvesAttachmentsIntoTheFragment()
    {
        // 多模态消息的图像必须随片段一起进入 PromptBuilder，否则图像在生成时被静默丢弃
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var message = CreateMsg(Sender.User, "look");
        message.Images = new List<MessageImage> { MessageImage.FromBase64("AAAA", "image/png") };
        env.Persistent.Chat.Messages.Add(message);
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].Message.Images.Should().ContainSingle()
            .Which.Base64.Should().Be("AAAA");
    }

    [Fact]
    public async Task MessageWithImages_DoesNotPolluteThePersistedMessage()
    {
        // 解析结果只存在于片段里：写回消息会把 Base64（或与存储方式不符的形态）持久化下来
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var message = CreateMsg(Sender.User, "look");
        message.Images = new List<MessageImage> { MessageImage.FromFile("abc.png", "image/png") };
        env.Persistent.Chat.Messages.Add(message);
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].Message.Should().NotBeSameAs(message.message);
        message.message.Images.Should().BeNull();
        message.Images[0].Base64.Should().BeNull();
    }

    [Fact]
    public async Task MessageWithoutImages_KeepsTheLiveMessageReference()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var message = CreateMsg(Sender.User, "plain");
        env.Persistent.Chat.Messages.Add(message);
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].Message.Should().BeSameAs(message.message);
    }

    [Fact]
    public async Task UnresolvableImage_FailsInsteadOfDroppingTheImage()
    {
        ImageAttachmentsMock
            .Setup(s => s.ToChatImageAsync(It.IsAny<MessageImage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("missing"));

        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var message = CreateMsg(Sender.User, "look");
        message.Images = new List<MessageImage> { MessageImage.FromFile("missing.png", "image/png") };
        env.Persistent.Chat.Messages.Add(message);
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingAttachmentService_FailsInsteadOfDroppingTheImage()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
            Chat = new Chat { Name = "TestChat" },
            Agent = Agent.Create("TestAgent", "")
        });
        var message = CreateMsg(Sender.User, "look");
        message.Images = new List<MessageImage> { MessageImage.FromBase64("AAAA", "image/png") };
        env.Persistent.Chat.Messages.Add(message);
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ServiceError);
    }

    [Fact]
    public async Task MessageAddedToChatAfterEnvBuild_IsAppended()
    {
        // 节点在执行时实时读取 Chat.Messages：环境构建（旧实现在此处 ToList 快照）之后
        // 对 chat.Messages 的修改也必须被感知，而不是使用构建时的副本。
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var ctx = CreateContext(env);

        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.User, "late"));

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("late");
    }

    private static Message AssistantWithCalls(params string[] ids)
    {
        var message = CreateMsg(Sender.AI, "");
        message.message.toolCalls = ids
            .Select(id => new SharperLLM.API.ToolCall { id = id, name = "ask_user", arguments = "{}" })
            .ToList();
        return message;
    }

    [Fact]
    public async Task UnansweredToolCall_GetsAPlaceholderResultAfterTheToolRun()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(CreateMsg(Sender.User, "go"));
        env.Persistent.Chat.Messages.Add(AssistantWithCalls("c1", "c2"));
        env.Persistent.Chat.Messages.Add(new Message
        {
            sender = Sender.ToolResult,
            timestamp = DateTime.UtcNow,
            message = new ChatMessage { Content = "done", id = "c1" }
        });
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();

        // user, assistant, tool(c1), placeholder(c2) —— 占位落在整段工具结果之后
        ctx.Env.Transient.Fragments.Should().HaveCount(4);
        var placeholder = ctx.Env.Transient.Fragments[3];
        placeholder.From.Should().Be(PromptBuilder.From.tool_result);
        placeholder.Message.id.Should().Be("c2");
        placeholder.Message.Content.Should().Contain("no_result");

        // 只改 prompt，不动对话
        env.Persistent.Chat.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task FullyAnsweredRound_AddsNoPlaceholder()
    {
        var node = new AppendChatMessagesNode();
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(AssistantWithCalls("c1"));
        env.Persistent.Chat.Messages.Add(new Message
        {
            sender = Sender.ToolResult,
            timestamp = DateTime.UtcNow,
            message = new ChatMessage { Content = "done", id = "c1" }
        });
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailFastPolicy_ReportsTheDanglingCalls()
    {
        var node = new AppendChatMessagesNode { DanglingToolCalls = DanglingToolCallPolicy.FailFast };
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Persistent.Chat.Messages.Add(AssistantWithCalls("c1"));
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        result.Message.Should().Contain("ask_user");
    }
}
