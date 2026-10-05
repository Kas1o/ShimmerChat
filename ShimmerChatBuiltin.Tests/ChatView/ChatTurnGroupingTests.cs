using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.ChatView.Aggregated;

namespace ShimmerChatBuiltin.Tests.ChatView;

/// <summary>
/// 聚合对话界面的分组规则测试。
/// 规则：连续的 AI / 工具结果消息合并为一个回合；AI 消息的正文与工具调用分离——
/// 正文留在气泡里，工具调用（无论一次还是多次、周围有没有别的调用）统一并入折叠的活动块。
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

    private static ChatDisplayUnit SingleTurn(IReadOnlyList<Message> messages)
        => ChatTurnGrouping.Build(messages).Single(u => u.IsTurn);

    private static ChatTextBlock AsText(ChatTurnBlock block)
    {
        block.Should().BeOfType<ChatTextBlock>();
        return (ChatTextBlock)block;
    }

    private static ChatToolActivityBlock AsActivity(ChatTurnBlock block)
    {
        block.Should().BeOfType<ChatToolActivityBlock>();
        return (ChatToolActivityBlock)block;
    }

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
    public void Build_SplitsToolCallsOutOfAnAssistantMessageWithContent()
    {
        // mes1{正文 + 工具调用} mes2{工具结果} mes3{工具调用} mes4{工具结果} mes5{正文}
        var turn = SingleTurn([
            Assistant("let me check", "thinking", ("c1", "read_file")),
            ToolResult("body", "c1"),
            Assistant("", null, ("c2", "grep")),
            ToolResult("matches", "c2"),
            Assistant("here is the answer")
        ]);

        turn.Blocks.Should().HaveCount(3);

        // mes1 的正文单独成块
        AsText(turn.Blocks[0]).Assistant.CurrentVersion!.Content.Should().Be("let me check");

        // mes1 的工具调用被剥离出来，与 mes3 的调用合并进下方同一块活动
        var activity = AsActivity(turn.Blocks[1]);
        activity.Interactions.Should().HaveCount(2);
        activity.ToolCount.Should().Be(2);
        activity.Interactions[0].Assistant!.CurrentVersion!.Content.Should().Be("let me check");
        activity.Interactions[0].ToolResults.Single().ToolName.Should().Be("read_file");
        activity.Interactions[1].Assistant!.CurrentVersion!.Content.Should().Be("");
        activity.Interactions[1].ToolResults.Single().ToolName.Should().Be("grep");

        // 最终回答仍然是正文块
        AsText(turn.Blocks[2]).Assistant.CurrentVersion!.Content.Should().Be("here is the answer");
    }

    [Fact]
    public void Build_CollectsIsolatedToolInteractionIntoActivity()
    {
        // 周围没有其它工具调用的单独一次调用同样归入活动块（正文气泡不再内联工具调用）
        var turn = SingleTurn([
            Assistant("first answer"),
            Assistant("", null, ("c1", "read_file")),
            ToolResult("body", "c1"),
            Assistant("second answer")
        ]);

        turn.Blocks.Should().HaveCount(3);
        AsText(turn.Blocks[0]);
        AsText(turn.Blocks[2]);

        var activity = AsActivity(turn.Blocks[1]);
        activity.Interactions.Should().HaveCount(1);
        activity.ToolCount.Should().Be(1);
    }

    [Fact]
    public void Build_MergesConsecutiveToolInteractionsIntoOneActivityBlock()
    {
        var turn = SingleTurn([
            Assistant("", "thinking 1", ("t1", "read_file")),
            ToolResult("r1", "t1"),
            Assistant("", "thinking 2", ("t2", "grep")),
            ToolResult("r2", "t2"),
            Assistant("final answer")
        ]);

        turn.Blocks.Should().HaveCount(2);

        var activity = AsActivity(turn.Blocks[0]);
        activity.Interactions.Should().HaveCount(2);
        activity.ToolCount.Should().Be(2);

        AsText(turn.Blocks[1]);
    }

    [Fact]
    public void Build_ActivityIsOpenWhileGenerating()
    {
        var generating = Assistant("", "thinking", ("t1", "read_file"));
        generating.GenerationState = MessageGenerationState.Generating;

        AsActivity(SingleTurn([generating, ToolResult("body", "t1")]).Blocks.Single())
            .IsOpen.Should().BeTrue();

        generating.GenerationState = MessageGenerationState.Completed;
        AsActivity(SingleTurn([generating, ToolResult("body", "t1")]).Blocks.Single())
            .IsOpen.Should().BeFalse("生成结束后活动块自动折叠，保持画面整洁");
    }

    [Fact]
    public void Build_KeepsAssistantWithoutContentAndWithoutToolCallsVisible()
    {
        var turn = SingleTurn([Assistant(""), Assistant("answer")]);

        turn.Blocks.Should().HaveCount(2);
        AsText(turn.Blocks[0]);
        AsText(turn.Blocks[1]);
    }

    [Fact]
    public void Build_TurnWithOnlyToolInteractionsHasJustTheActivityBlock()
    {
        var turn = SingleTurn([
            Assistant("", null, ("t1", "read_file")),
            ToolResult("r1", "t1")
        ]);

        AsActivity(turn.Blocks.Single()).Interactions.Should().HaveCount(1);
    }

    [Fact]
    public void Build_ResolvesToolNameFromToolCallId()
    {
        var turn = SingleTurn([
            Assistant("", null, ("call-1", "read_file")),
            ToolResult("content", "call-1")
        ]);

        AsActivity(turn.Blocks.Single()).Interactions.Single()
            .ToolResults.Single().ToolName.Should().Be("read_file");
    }

    [Fact]
    public void Build_FallsBackToPositionalToolNameWhenIdsAreMissing()
    {
        var turn = SingleTurn([
            Assistant("", null, ("", "write_file")),
            ToolResult("ok", "")
        ]);

        AsActivity(turn.Blocks.Single()).Interactions.Single()
            .ToolResults.Single().ToolName.Should().Be("write_file");
    }

    [Fact]
    public void Build_KeepsOrphanToolResultsInActivityWithoutAssistant()
    {
        var interaction = AsActivity(SingleTurn([ToolResult("orphan result", "unknown-id")]).Blocks.Single())
            .Interactions.Single();

        interaction.Assistant.Should().BeNull();
        interaction.ToolResults.Single().ToolName.Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsEmptyForEmptyChat()
    {
        ChatTurnGrouping.Build([]).Should().BeEmpty();
    }
}
