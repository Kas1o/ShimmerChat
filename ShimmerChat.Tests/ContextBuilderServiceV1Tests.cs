using FluentAssertions;
using Moq;
using SharperLLM.Util;
using ShimmerChat.Singletons;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

public class ContextBuilderServiceV1Tests
{
    private readonly Mock<IContextModifierService> _modifierMock = new();
    private readonly ContextBuilderServiceV1 _service;

    public ContextBuilderServiceV1Tests()
    {
        _modifierMock
            .Setup(m => m.ApplyModifiers(It.IsAny<ContextDocument>(), It.IsAny<Chat>(), It.IsAny<Agent>()))
            .Callback<ContextDocument, Chat, Agent>((ctx, _, _) =>
            {
                // Default: no modification - leave context as-is
            });
        _service = new ContextBuilderServiceV1(_modifierMock.Object);
    }

    private static (Chat, Agent) CreateChatAndAgent()
    {
        var agent = Agent.Create("TestAgent", "system prompt");
        var chat = new Chat { Name = "TestChat" };
        return (chat, agent);
    }

    private static Message CreateMessage(string sender, string content)
    {
        var msg = new Message { sender = sender, timestamp = DateTime.UtcNow };
        msg.message = new ChatMessage { Content = content };
        return msg;
    }

    [Fact]
    public void BuildContextDocument_InsertsSystemPrompt()
    {
        var (chat, agent) = CreateChatAndAgent();

        var result = _service.BuildContextDocument(chat, agent);

        var messages = result.GetMessages();
        messages.Should().NotBeEmpty();
        messages[0].Item2.Should().Be(PromptBuilder.From.system);
        messages[0].Item1.Content.Should().Be("system prompt");
    }

    [Fact]
    public void BuildContextDocument_MapsUserMessage()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.User, "hello"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages.Should().HaveCount(2);
        messages[1].Item2.Should().Be(PromptBuilder.From.user);
        messages[1].Item1.Content.Should().Be("hello");
    }

    [Fact]
    public void BuildContextDocument_MapsAIMessage()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.AI, "response"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages[1].Item2.Should().Be(PromptBuilder.From.assistant);
    }

    [Fact]
    public void BuildContextDocument_MapsSystemMessage()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.System, "sys msg"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages[1].Item2.Should().Be(PromptBuilder.From.system);
    }

    [Fact]
    public void BuildContextDocument_MapsToolResultMessage()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.ToolResult, "tool output"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages[1].Item2.Should().Be(PromptBuilder.From.tool_result);
    }

    [Fact]
    public void BuildContextDocument_FiltersRegeneratingMessages()
    {
        var (chat, agent) = CreateChatAndAgent();
        var regeneratingMsg = CreateMessage(Sender.AI, "should be filtered");
        regeneratingMsg.GenerationState = MessageGenerationState.Regenerating;
        chat.AddMessage(regeneratingMsg);
        chat.AddMessage(CreateMessage(Sender.User, "visible"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages.Should().HaveCount(2);
        messages[1].Item1.Content.Should().Be("visible");
    }

    [Fact]
    public void BuildContextDocument_IncludesMetadata()
    {
        var (chat, agent) = CreateChatAndAgent();
        var now = DateTime.UtcNow;
        var msg = CreateMessage(Sender.User, "test");
        msg.timestamp = now;
        chat.AddMessage(msg);

        var result = _service.BuildContextDocument(chat, agent);

        var segment = result.Segments[1];
        segment.Metadata.Should().ContainKey("timestamp");
        segment.Metadata.Should().ContainKey("sender");
        segment.Metadata["sender"].Should().Be(Sender.User);
    }

    [Fact]
    public void BuildContextDocument_AppliesModifiers()
    {
        var (chat, agent) = CreateChatAndAgent();

        _service.BuildContextDocument(chat, agent);

        _modifierMock.Verify(
            m => m.ApplyModifiers(It.IsAny<ContextDocument>(), chat, agent),
            Times.Once);
    }

    [Fact]
    public void BuildPromptBuilder_ReturnsPromptBuilder()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.User, "hello"));

        var pb = _service.BuildPromptBuilder(chat, agent);

        pb.Messages.Should().NotBeNull();
        pb.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void BuildPromptBuilderWithTools_IncludesToolDefinitions()
    {
        var (chat, agent) = CreateChatAndAgent();
        var tools = new List<SharperLLM.FunctionCalling.Tool>
        {
            new() { name = "tool1", description = "test tool" }
        };

        var pb = _service.BuildPromptBuilderWithTools(chat, agent, tools);

        pb.AvailableTools.Should().HaveCount(1);
        pb.AvailableTools![0].name.Should().Be("tool1");
        pb.AvailableToolsFormatter.Should().NotBeNull();
    }

    [Fact]
    public void BuildPromptBuilderWithTools_NullTools_DoesNotSetTools()
    {
        var (chat, agent) = CreateChatAndAgent();

        var pb = _service.BuildPromptBuilderWithTools(chat, agent, null!);

        pb.AvailableTools.Should().BeNull();
    }

    [Fact]
    public void BuildPromptBuilderWithTools_EmptyTools_DoesNotSetTools()
    {
        var (chat, agent) = CreateChatAndAgent();

        var pb = _service.BuildPromptBuilderWithTools(chat, agent, new List<SharperLLM.FunctionCalling.Tool>());

        pb.AvailableTools.Should().BeNull();
    }

    [Fact]
    public void BuildPromptBuilderWithoutContextModify_DoesNotApplyModifiers()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.User, "hello"));

        var pb = _service.BuildPromptBuilderWithoutContextModify(chat, agent);

        _modifierMock.Verify(
            m => m.ApplyModifiers(It.IsAny<ContextDocument>(), It.IsAny<Chat>(), It.IsAny<Agent>()),
            Times.Never);
        pb.Messages.Should().HaveCount(2);
        pb.Messages[0].Item1.Content.Should().Be("system prompt");
    }

    [Fact]
    public void BuildPromptBuilderForContinuation_SetsPrefixOnTargetMessage()
    {
        var (chat, agent) = CreateChatAndAgent();
        var aiMessage = CreateMessage(Sender.AI, "AI response");
        chat.AddMessage(CreateMessage(Sender.User, "user input"));
        chat.AddMessage(aiMessage);

        var pb = _service.BuildPromptBuilderForContinuation(chat, agent, new List<SharperLLM.FunctionCalling.Tool>(), aiMessage);

        var aiMsgInPb = pb.Messages.FirstOrDefault(m => m.Item2 == PromptBuilder.From.assistant);
        aiMsgInPb.Should().NotBeNull();
        var hasPrefix = aiMsgInPb!.Item1.CustomProperties?.ContainsKey("prefix") == true
            && aiMsgInPb.Item1.CustomProperties["prefix"] is true;
        hasPrefix.Should().BeTrue("continuation message should have prefix=true");
    }

    [Fact]
    public void BuildPromptBuilderForContinuation_AppliesModifiers()
    {
        var (chat, agent) = CreateChatAndAgent();
        var aiMessage = CreateMessage(Sender.AI, "AI response");
        chat.AddMessage(aiMessage);

        _service.BuildPromptBuilderForContinuation(chat, agent, new List<SharperLLM.FunctionCalling.Tool>(), aiMessage);

        _modifierMock.Verify(
            m => m.ApplyModifiers(It.IsAny<ContextDocument>(), chat, agent),
            Times.Once);
    }

    [Fact]
    public void BuildContextDocument_WithMultipleMessages_PreservesOrder()
    {
        var (chat, agent) = CreateChatAndAgent();
        chat.AddMessage(CreateMessage(Sender.User, "first"));
        chat.AddMessage(CreateMessage(Sender.AI, "second"));
        chat.AddMessage(CreateMessage(Sender.User, "third"));

        var result = _service.BuildContextDocument(chat, agent);
        var messages = result.GetMessages();

        messages.Should().HaveCount(4);
        messages[0].Item1.Content.Should().Be("system prompt");
        messages[1].Item1.Content.Should().Be("first");
        messages[2].Item1.Content.Should().Be("second");
        messages[3].Item1.Content.Should().Be("third");
    }
}
