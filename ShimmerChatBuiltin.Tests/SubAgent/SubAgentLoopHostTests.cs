using SharperLLM.Agents;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// SubAgentLoopHost 的增量消息回调：每条 assistant / tool_result 都要在重建环境之前上报一次。
/// </summary>
public class SubAgentLoopHostTests
{
    private sealed class StubToolExecutor : IToolExecutor
    {
        public Task<string?> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
            => Task.FromResult<string?>("tool output");
    }

    [Fact]
    public async Task AssistantAndToolResults_AreReportedToCallback()
    {
        var appended = new List<(ChatMessage Message, PromptBuilder.From From)>();
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            new StubToolExecutor(),
            postProcessor: null,
            rebuildFragments: null,
            onMessageAppended: appended.Add);

        await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.Stop
        }, CancellationToken.None);

        await host.OnToolCompleteAsync(new ToolCall { id = "call-1", name = "tool" }, "result", CancellationToken.None);

        appended.Should().HaveCount(2);
        appended[0].From.Should().Be(PromptBuilder.From.assistant);
        appended[0].Message.Content.Should().Be("answer");
        appended[1].From.Should().Be(PromptBuilder.From.tool_result);
        appended[1].Message.Content.Should().Be("result");
        appended[1].Message.id.Should().Be("call-1");

        // 种子消息 + assistant + tool_result
        host.CurrentContext.Messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task CallbackRunsBeforeRebuild_AndRebuildSeesIncrement()
    {
        var order = new List<string>();
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            new StubToolExecutor(),
            postProcessor: null,
            rebuildFragments: conversation =>
            {
                order.Add("rebuild");
                conversation.Should().ContainSingle();
                return Task.FromResult(new List<ContextSegment>());
            },
            onMessageAppended: _ => order.Add("append"));

        await host.OnToolCompleteAsync(new ToolCall { id = "call-1", name = "tool" }, "result", CancellationToken.None);

        order.Should().Equal("append", "rebuild");
    }

    [Fact]
    public async Task WithoutCallback_LoopStillWorks()
    {
        var host = new SubAgentLoopHost(
            new SubAgentPromptContext("task"),
            new StubToolExecutor(),
            postProcessor: null,
            rebuildFragments: null);

        await host.OnAssistantCompleteAsync(new ResponseEx
        {
            Body = new ChatMessage { Content = "answer" },
            FinishReason = FinishReason.Stop
        }, CancellationToken.None);

        host.CurrentContext.Messages.Should().HaveCount(2);
    }
}
