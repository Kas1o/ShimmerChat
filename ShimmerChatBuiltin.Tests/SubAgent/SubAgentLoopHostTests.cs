using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// SubAgentLoopHost 契约：后处理轮次的 assistant 消息与节点追加的消息都要在上报后进入对话增量，
/// 且只有循环指令为 Continue 时才重建环境。
/// </summary>
public class SubAgentLoopHostTests
{
    private static SubAgentPostRound Round(string answer, PostLoopDirective directive,
        params Message[] appended) => new()
        {
            ResponseMessage = new ChatMessage { Content = answer },
            LoopDirective = directive,
            Appended = appended
        };

    private static Message ToolResult(string content, string id) => new()
    {
        sender = Sender.ToolResult,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content, id = id }
    };

    [Fact]
    public async Task AssistantAndAppendedMessages_AreReportedToCallback()
    {
        var appended = new List<(ChatMessage Message, PromptBuilder.From From)>();
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            postProcessor: _ => Task.FromResult(Round("answer", PostLoopDirective.Stop, ToolResult("tool output", "call-1"))),
            rebuildFragments: null,
            onMessageAppended: appended.Add);

        var continued = await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.Stop
        }, CancellationToken.None);

        continued.Should().BeFalse();

        appended.Should().HaveCount(2);
        appended[0].From.Should().Be(PromptBuilder.From.assistant);
        appended[0].Message.Content.Should().Be("answer");
        appended[1].From.Should().Be(PromptBuilder.From.tool_result);
        appended[1].Message.Content.Should().Be("tool output");
        appended[1].Message.id.Should().Be("call-1");

        // 种子消息 + assistant + tool_result（无重建回调，直接追加到上下文）
        host.CurrentContext.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task Continue_RebuildsAfterAppending()
    {
        var order = new List<string>();
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            postProcessor: _ => Task.FromResult(Round("answer", PostLoopDirective.Continue, ToolResult("result", "call-1"))),
            rebuildFragments: conversation =>
            {
                order.Add("rebuild");
                conversation.Should().HaveCount(2);
                return Task.FromResult(new List<ContextSegment>());
            },
            onMessageAppended: _ => order.Add("append"));

        var continued = await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.FunctionCall
        }, CancellationToken.None);

        continued.Should().BeTrue();
        order.Should().Equal("append", "append", "rebuild");
    }

    [Fact]
    public async Task Stop_DoesNotRebuild()
    {
        var rebuildCalls = 0;
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            postProcessor: _ => Task.FromResult(Round("answer", PostLoopDirective.Stop)),
            rebuildFragments: _ =>
            {
                rebuildCalls++;
                return Task.FromResult(new List<ContextSegment>());
            });

        var continued = await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.Stop
        }, CancellationToken.None);

        continued.Should().BeFalse();
        rebuildCalls.Should().Be(0);
    }

    [Fact]
    public async Task WithoutPostProcessor_UsesRawResponse()
    {
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            postProcessor: null,
            rebuildFragments: null);

        var continued = await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.Stop
        }, CancellationToken.None);

        continued.Should().BeFalse();
        host.CurrentContext.Messages.Should().HaveCount(2);
        host.CurrentContext.LastAssistantContent.Should().Be("answer");
    }
}
