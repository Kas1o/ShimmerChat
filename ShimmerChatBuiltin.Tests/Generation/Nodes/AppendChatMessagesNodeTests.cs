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
        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["ChatMessages"] = new List<Message> { CreateMsg(Sender.User, "hello") };
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
        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["ChatMessages"] = new List<Message> { CreateMsg(Sender.AI, "reply") };
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.assistant);
    }

    [Fact]
    public async Task RegeneratingMessage_Skipped()
    {
        var node = new AppendChatMessagesNode();
        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["ChatMessages"] = new List<Message>
        {
            CreateMsg(Sender.User, "skip", MessageGenerationState.Regenerating),
            CreateMsg(Sender.AI, "keep")
        };
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("keep");
    }

    [Fact]
    public async Task MultipleMessages_AllAppended()
    {
        var node = new AppendChatMessagesNode();
        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["ChatMessages"] = new List<Message>
        {
            CreateMsg(Sender.System, "s"),
            CreateMsg(Sender.User, "u"),
            CreateMsg(Sender.AI, "a"),
            CreateMsg(Sender.ToolResult, "t")
        };
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(4);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.system);
        ctx.Env.Transient.Fragments[1].From.Should().Be(PromptBuilder.From.user);
        ctx.Env.Transient.Fragments[2].From.Should().Be(PromptBuilder.From.assistant);
        ctx.Env.Transient.Fragments[3].From.Should().Be(PromptBuilder.From.tool_result);
    }
}
