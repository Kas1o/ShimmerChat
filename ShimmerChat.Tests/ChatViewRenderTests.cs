using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.ChatView.Aggregated;
using ShimmerChatBuiltin.ChatView.Standard;
using ShimmerChatLib;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 内置对话界面的渲染冒烟测试：用静态 HTML 渲染器把两个界面真正渲染一遍，
/// 验证宿主传入的上下文契约、注入的服务与分组结果都能落到 DOM 上。
/// </summary>
public class ChatViewRenderTests
{
    private static Message UserMessage(string content) => new()
    {
        sender = Sender.User,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content }
    };

    private static Message AssistantMessage(string content, params (string id, string name)[] toolCalls) => new()
    {
        sender = Sender.AI,
        timestamp = DateTime.Now,
        message = new ChatMessage
        {
            Content = content,
            toolCalls = toolCalls.Length == 0
                ? null
                : toolCalls.Select(tc => new ToolCall { id = tc.id, name = tc.name, arguments = "{}" }).ToList()
        }
    };

    private static Message ToolResultMessage(string content, string id) => new()
    {
        sender = Sender.ToolResult,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content, id = id }
    };

    private static Chat BuildChat(params Message[] messages)
    {
        var chat = new Chat { Name = "chat" };
        foreach (var message in messages)
            chat.AddMessage(message);
        return chat;
    }

    private static ChatViewContext BuildContext(Chat chat) => new()
    {
        Chat = chat,
        Agent = Agent.Create("TestAgent", "intro"),
        IsGenerating = () => false,
        GenerationPhase = () => null,
        SendAsync = _ => Task.CompletedTask,
        StopGenerationAsync = () => Task.CompletedTask,
        DeleteMessageAsync = _ => Task.CompletedTask,
        DeleteMessagesFromAsync = _ => Task.CompletedTask,
        RegenerateFromAsync = _ => Task.CompletedTask,
        ContinueFromAsync = _ => Task.CompletedTask,
        MarkDirty = () => { },
        RequestRefreshAsync = () => Task.CompletedTask,
        NavigateBackAsync = () => Task.CompletedTask
    };

    /// <summary>聚合场景：一个回合经过两次「无输出」工具调用后给出最终回答。</summary>
    private static Chat BuildToolLoopChat() => BuildChat(
        UserMessage("ask"),
        AssistantMessage("", ("c1", "read_file")),
        ToolResultMessage("file body", "c1"),
        AssistantMessage("", ("c2", "grep")),
        ToolResultMessage("grep body", "c2"),
        AssistantMessage("final answer"));

    private static async Task<string> RenderAsync<TComponent>(ChatViewContext context)
        where TComponent : IComponent
    {
        using var provider = BuildServices();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Context"] = context }));
            return output.ToHtmlString();
        });
    }

    private static ServiceProvider BuildServices()
    {
        var display = new Mock<IMessageDisplayService>();
        display
            .Setup(d => d.Render(It.IsAny<string>(), It.IsAny<Chat?>(), It.IsAny<Agent?>()))
            .Returns((string text, Chat? _, Agent? _) => new MarkupString(WebUtility.HtmlEncode(text ?? string.Empty)));

        var loc = new Mock<ILocService>();
        loc.SetupGet(l => l.CurrentCulture).Returns("zh-CN");
        loc.SetupGet(l => l.SupportedCultures).Returns(["zh-CN"]);
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(key => key);
        loc.Setup(l => l.Format(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((key, args) => string.Format(key, args));

        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read(It.IsAny<string>(), It.IsAny<string>())).Returns((string?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(display.Object);
        services.AddSingleton(loc.Object);
        services.AddSingleton(kv.Object);
        services.AddSingleton(Mock.Of<IMessageStoreService>());
        services.AddSingleton(Mock.Of<IPopupService>());
        services.AddSingleton(Mock.Of<IJSRuntime>());
        return services.BuildServiceProvider();
    }

    private static int CountOccurrences(string html, string needle)
    {
        int count = 0, index = 0;
        while ((index = html.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task StandardChatView_RendersOneBubblePerMessage()
    {
        var html = await RenderAsync<StandardChatView>(BuildContext(BuildToolLoopChat()));

        CountOccurrences(html, "chat-msg chat-msg-user").Should().Be(1);
        CountOccurrences(html, "chat-msg chat-msg-ai").Should().Be(3);
        CountOccurrences(html, "chat-msg chat-msg-tool").Should().Be(2);
        html.Should().NotContain("chat-turn");
        html.Should().NotContain("chat-activity");

        html.Should().Contain("final answer");
        html.Should().Contain("TestAgent");
    }

    [Fact]
    public async Task AggregatedChatView_MergesToolLoopIntoOneBubbleWithCollapsedActivity()
    {
        var html = await RenderAsync<AggregatedChatView>(BuildContext(BuildToolLoopChat()));

        // 用户消息仍然独立显示，整个工具调用循环合并为一个回合气泡
        CountOccurrences(html, "chat-msg chat-msg-user").Should().Be(1);
        CountOccurrences(html, "chat-turn\"").Should().Be(1);

        // 连续两个无输出步骤合并为一块默认折叠的工具活动
        CountOccurrences(html, "<details class=\"chat-activity\">").Should().Be(1);
        html.Should().Contain("chatview.agg.tool_activity");
        html.Should().Contain("read_file");
        html.Should().Contain("grep");

        // 最终回答与工具内容都仍然渲染出来（折叠只影响展示，不丢内容）
        html.Should().Contain("final answer");
        html.Should().Contain("file body");

        // 回合的收尾步骤必须仍可重新生成（合并气泡不能吞掉这个操作）
        CountOccurrences(html, "BUTTON_REGENERATE").Should().Be(1);
    }

    [Fact]
    public async Task AggregatedChatView_MovesToolCallsOfATextMessageIntoTheActivityBelow()
    {
        // mes1{正文 + 工具调用} mes2{工具结果} mes3{工具调用} mes4{工具结果} mes5{正文}
        var chat = BuildChat(
            UserMessage("ask"),
            AssistantMessage("let me check", ("c1", "read_file")),
            ToolResultMessage("file body", "c1"),
            AssistantMessage("", ("c2", "grep")),
            ToolResultMessage("grep body", "c2"),
            AssistantMessage("final answer"));

        var html = await RenderAsync<AggregatedChatView>(BuildContext(chat));

        CountOccurrences(html, "chat-turn\"").Should().Be(1);
        CountOccurrences(html, "<details class=\"chat-activity\">").Should().Be(1);

        // 同一条消息的正文只渲染一次：一次在正文块里，活动块里只剩它的工具调用
        CountOccurrences(html, "let me check").Should().Be(1);
        CountOccurrences(html, "final answer").Should().Be(1);

        // 排版顺序：mes1 的正文 → 工具调用活动（含 mes1 自己的调用）→ 最终回答
        int textIndex = html.IndexOf("let me check", StringComparison.Ordinal);
        int activityIndex = html.IndexOf("<details class=\"chat-activity\"", StringComparison.Ordinal);
        int answerIndex = html.IndexOf("final answer", StringComparison.Ordinal);

        textIndex.Should().BeGreaterThanOrEqualTo(0);
        activityIndex.Should().BeGreaterThan(textIndex);
        answerIndex.Should().BeGreaterThan(activityIndex);

        // 工具调用的参数只在活动块内出现，正文气泡不再内联
        html.IndexOf("<summary class=\"tc-summary\">", StringComparison.Ordinal).Should().BeGreaterThan(activityIndex);
        html.IndexOf("read_file", StringComparison.Ordinal).Should().BeGreaterThan(activityIndex);
        html.IndexOf("grep", StringComparison.Ordinal).Should().BeGreaterThan(activityIndex);
    }

    [Fact]
    public async Task AggregatedChatView_SplitsOnUserMessages()
    {
        var chat = BuildChat(
            UserMessage("first"),
            AssistantMessage("answer one"),
            UserMessage("second"),
            AssistantMessage("answer two"));

        var html = await RenderAsync<AggregatedChatView>(BuildContext(chat));

        CountOccurrences(html, "chat-msg chat-msg-user").Should().Be(2);
        CountOccurrences(html, "chat-turn\"").Should().Be(2);
        CountOccurrences(html, "<details class=\"chat-activity\">").Should().Be(0);
    }
}
