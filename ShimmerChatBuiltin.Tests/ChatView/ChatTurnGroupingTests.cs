using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.ChatView.Aggregated;

namespace ShimmerChatBuiltin.Tests.ChatView;

/// <summary>
/// 聚合对话界面的分组规则测试。
/// 规则：连续的 AI / 工具结果消息合并为一个回合；回合内「无输出」（正文为空，
/// 只有思考与工具调用）的步骤默认合并折叠；回合最后一步永不折叠。
/// </summary>
public class ChatTurnGroupingTests
{
    private static Message UserMessage(string content) => new()
    {
        sender = Sender.User,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content }
    };

    private static Message Assistant(string content = "", string? thinking = null, params (string id, string name)[] toolCalls) => new()
    {
        sender = Sender.AI,
        timestamp = DateTime.Now,
        message = new ChatMessage
        {
            Content = content,
            thinking = thinking,
            toolCalls = toolCalls.Length == 0
                ? null
                : toolCalls.Select(tc => new ToolCall { id = tc.id, name = tc.name, arguments = "{}" }).ToList()
        }
    };

    private static Message ToolResult(string content, string id) => new()
    {
        sender = Sender.ToolResult,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content, id = id }
    };

    [Fact]
    public void Build_MergesConsecutiveAssistantAndToolMessagesIntoOneTurn()
    {
        var units = ChatTurnGrouping.Build([
            UserMessage("hi"),
            Assistant("look", null, ("t1", "read_file")),
            ToolResult("file content", "t1"),
            Assistant("done"),
            UserMessage("thanks")
        ]);

        units.Should().HaveCount(3);
        units[0].IsTurn.Should().BeFalse();
        units[0].Messages.Single().sender.Should().Be(Sender.User);

        units[1].IsTurn.Should().BeTrue();
        units[1].Messages.Should().HaveCount(3);
        units[1].Blocks.Should().HaveCount(2);

        units[2].IsTurn.Should().BeFalse();
    }

    [Fact]
    public void Build_UserMessageSplitsTurns()
    {
        var units = ChatTurnGrouping.Build([
            Assistant("first"),
            UserMessage("again"),
            Assistant("second")
        ]);

        units.Should().HaveCount(3);
        units.Where(u => u.IsTurn).Should().HaveCount(2);
    }

    [Fact]
    public void Build_KeepsLastStepOfTurnExpanded()
    {
        var units = ChatTurnGrouping.Build([
            UserMessage("do it"),
            Assistant("", null, ("t1", "read_file")),
            ToolResult("r1", "t1"),
            Assistant("", null, ("t2", "read_file")),
            ToolResult("r2", "t2")
        ]);

        var turn = units.Single(u => u.IsTurn);

        // 倒数第二步仍然折叠（无输出），最后一步强制展开，避免用户看不到结果
        turn.Blocks.Should().HaveCount(2);
        turn.Blocks[0].IsActivity.Should().BeTrue();
        turn.Blocks[0].Steps.Should().HaveCount(1);
        turn.Blocks[1].IsActivity.Should().BeFalse();
        turn.Blocks[1].Steps.Single().IsSilent.Should().BeFalse();
    }

    [Fact]
    public void Build_DoesNotCollapseTurnThatIsNotAtTheEndOfChat()
    {
        // 回合被后续用户消息截断时不享受「最后一步」特权，静默步骤全部折叠
        var units = ChatTurnGrouping.Build([
            Assistant("", null, ("t1", "read_file")),
            ToolResult("r1", "t1"),
            UserMessage("next")
        ]);

        var turn = units.Single(u => u.IsTurn);
        turn.Blocks.Should().HaveCount(1);
        turn.Blocks[0].IsActivity.Should().BeTrue();
        turn.Blocks[0].Steps.Single().IsSilent.Should().BeTrue();
    }

    [Fact]
    public void Build_MergesConsecutiveSilentStepsIntoOneActivityBlock()
    {
        var units = ChatTurnGrouping.Build([
            Assistant("", "thinking 1", ("t1", "read_file")),
            ToolResult("r1", "t1"),
            Assistant("", "thinking 2", ("t2", "grep")),
            ToolResult("r2", "t2"),
            Assistant("final answer")
        ]);

        var turn = units.Single(u => u.IsTurn);

        turn.Blocks.Should().HaveCount(2);
        turn.Blocks[0].IsActivity.Should().BeTrue();
        turn.Blocks[0].Steps.Should().HaveCount(2);
        turn.Blocks[0].ToolCount.Should().Be(2);

        // 最后一个块是正文回答，保持展开
        turn.Blocks[1].IsActivity.Should().BeFalse();
        turn.Blocks[1].Steps.Single().Assistant!.CurrentVersion!.Content.Should().Be("final answer");
    }

    [Fact]
    public void Build_KeepsStepWithVisibleContentExpanded()
    {
        var units = ChatTurnGrouping.Build([
            Assistant("let me check", null, ("t1", "read_file")),
            ToolResult("r1", "t1"),
            Assistant("answer")
        ]);

        var turn = units.Single(u => u.IsTurn);
        turn.Blocks.Should().HaveCount(2);
        turn.Blocks[0].IsActivity.Should().BeFalse();
        turn.Blocks[0].Steps.Single().Assistant!.CurrentVersion!.Content.Should().Be("let me check");
        turn.Blocks[0].Steps.Single().ToolResults.Single().Message.CurrentVersion!.Content.Should().Be("r1");
    }

    [Fact]
    public void Build_KeepsStreamingStepExpanded()
    {
        var streaming = Assistant("", "thinking", ("t1", "read_file"));
        streaming.GenerationState = MessageGenerationState.Generating;

        var units = ChatTurnGrouping.Build([
            streaming,
            Assistant("previous answer")
        ]);

        var turn = units.Single(u => u.IsTurn);
        turn.Blocks.Should().HaveCount(2);
        turn.Blocks[0].IsActivity.Should().BeFalse();
        turn.Blocks[0].Steps.Single().IsSilent.Should().BeFalse();
    }

    [Fact]
    public void Build_ResolvesToolNameFromToolCallId()
    {
        var units = ChatTurnGrouping.Build([
            Assistant("", null, ("call-1", "read_file")),
            ToolResult("content", "call-1")
        ]);

        var toolResult = units.Single(u => u.IsTurn).Blocks.Single().Steps.Single().ToolResults.Single();

        toolResult.ToolName.Should().Be("read_file");
    }

    [Fact]
    public void Build_FallsBackToPositionalToolNameWhenIdsAreMissing()
    {
        var units = ChatTurnGrouping.Build([
            Assistant("", null, ("", "write_file")),
            ToolResult("ok", "")
        ]);

        var toolResult = units.Single(u => u.IsTurn).Blocks.Single().Steps.Single().ToolResults.Single();

        toolResult.ToolName.Should().Be("write_file");
    }

    [Fact]
    public void Build_LeavesToolNameNullWhenItCannotBeResolved()
    {
        var units = ChatTurnGrouping.Build([
            ToolResult("orphan result", "unknown-id")
        ]);

        var toolResult = units.Single(u => u.IsTurn).Blocks.Single().Steps.Single().ToolResults.Single();

        toolResult.ToolName.Should().BeNull();
        units.Single(u => u.IsTurn).Blocks.Single().Steps.Single().Assistant.Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsEmptyForEmptyChat()
    {
        ChatTurnGrouping.Build([]).Should().BeEmpty();
    }
}
