using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Moq;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests.ChatView;

/// <summary>
/// 消息渲染结果缓存：同一段文本/上下文只能执行一次渲染管线，
/// 内容或渲染上下文变化时必须失效，避免气泡重渲染时复用旧 HTML。
/// </summary>
public class MessageRenderCacheTests
{
    private static ChatViewContext BuildContext(Chat chat, Agent agent) => new()
    {
        Chat = chat,
        Agent = agent,
        IsGenerating = () => false,
        GenerationPhase = () => null,
        SendAsync = _ => Task.CompletedTask,
        SendWithImagesAsync = (_, _) => Task.CompletedTask,
        StopGenerationAsync = () => Task.CompletedTask,
        DeleteMessageAsync = _ => Task.CompletedTask,
        DeleteMessagesFromAsync = _ => Task.CompletedTask,
        RegenerateFromAsync = _ => Task.CompletedTask,
        ContinueFromAsync = _ => Task.CompletedTask,
        RenameAsync = _ => Task.FromResult(true),
        MarkDirty = () => { },
        RequestRefreshAsync = () => Task.CompletedTask,
        NavigateBackAsync = () => Task.CompletedTask
    };

    private static (Mock<IMessageDisplayService> Display, ChatViewContext Context) BuildSut()
    {
        var display = new Mock<IMessageDisplayService>();
        display
            .Setup(d => d.Render(It.IsAny<string>(), It.IsAny<Chat?>(), It.IsAny<Agent?>()))
            .Returns((string text, Chat? _, Agent? _) => new MarkupString($"[{text}]"));

        var chat = new Chat { Name = "chat" };
        var agent = Agent.Create("agent", "intro");
        return (display, BuildContext(chat, agent));
    }

    [Fact]
    public void Get_SameTextVersionAndContext_RendersOnce()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);
        var version = new ChatMessage { Content = "hello" };

        var first = cache.Get("hello", version, context);
        var second = cache.Get("hello", version, context);

        first.Value.Should().Be("[hello]");
        second.Value.Should().Be("[hello]");
        display.Verify(d => d.Render("hello", context.Chat, context.Agent), Times.Once);
    }

    [Fact]
    public void Get_ContentChanged_Rerenders()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);
        var version = new ChatMessage { Content = "hello" };

        cache.Get("hello", version, context);
        var updated = cache.Get("hello world", version, context);

        updated.Value.Should().Be("[hello world]");
        display.Verify(d => d.Render("hello", context.Chat, context.Agent), Times.Once);
        display.Verify(d => d.Render("hello world", context.Chat, context.Agent), Times.Once);
    }

    [Fact]
    public void Get_VersionChanged_Rerenders()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);

        cache.Get("same", new ChatMessage { Content = "same" }, context);
        cache.Get("same", new ChatMessage { Content = "same" }, context);

        display.Verify(d => d.Render("same", context.Chat, context.Agent), Times.Exactly(2));
    }

    [Fact]
    public void Get_ContextChanged_Rerenders()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);
        var version = new ChatMessage { Content = "same" };

        cache.Get("same", version, context);
        cache.Get("same", version, BuildContext(context.Chat, context.Agent));

        display.Verify(d => d.Render("same", It.IsAny<Chat?>(), It.IsAny<Agent?>()), Times.Exactly(2));
    }

    [Fact]
    public void Get_AgentRenderTreeChanged_Rerenders()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);
        var version = new ChatMessage { Content = "same" };

        cache.Get("same", version, context);
        context.Agent.RenderModifierTreeJson = "{\"type\":\"changed\"}";
        cache.Get("same", version, context);

        display.Verify(d => d.Render("same", context.Chat, context.Agent), Times.Exactly(2));
    }

    [Fact]
    public void Get_CultureChanged_Rerenders()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);
        var version = new ChatMessage { Content = "same" };

        cache.Get("same", version, context, "zh-CN");
        cache.Get("same", version, context, "en-US");

        display.Verify(d => d.Render("same", context.Chat, context.Agent), Times.Exactly(2));
    }

    [Fact]
    public void Get_NullThinking_RendersEmptyTextOnce()
    {
        var (display, context) = BuildSut();
        var cache = new MessageRenderCache(display.Object);

        cache.Get(null, null, context);
        cache.Get(null, null, context);

        display.Verify(d => d.Render(string.Empty, context.Chat, context.Agent), Times.Once);
    }
}
