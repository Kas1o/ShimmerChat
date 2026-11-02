using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 工具调用配对规则：历史完整性检查依赖它判断"哪些调用还没有结果"。
/// </summary>
public class ToolCallPairingTests
{
    private static Message Assistant(params string[] ids)
    {
        var message = new Message
        {
            sender = Sender.AI,
            timestamp = DateTime.Now,
            message = new ChatMessage { Content = "" }
        };

        if (ids.Length > 0)
        {
            message.message.toolCalls = ids
                .Select(id => new ToolCall { id = id, name = "tool", arguments = "{}" })
                .ToList();
        }

        return message;
    }

    private static Message ToolResult(string id, string content = "ok") => new()
    {
        sender = Sender.ToolResult,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content, id = id }
    };

    private static Message User(string content) => new()
    {
        sender = Sender.User,
        timestamp = DateTime.Now,
        message = new ChatMessage { Content = content }
    };

    [Fact]
    public void AnsweredCalls_AreNotReported()
    {
        var messages = new List<Message> { User("hi"), Assistant("c1", "c2"), ToolResult("c1"), ToolResult("c2") };

        ToolCallPairing.EnumerateUnanswered(messages).Should().BeEmpty();
        ToolCallPairing.AnyUnanswered(messages).Should().BeFalse();
    }

    [Fact]
    public void CallWithoutResult_IsReportedWithItsIndex()
    {
        var messages = new List<Message> { Assistant("c1", "c2"), ToolResult("c1") };

        var unanswered = ToolCallPairing.EnumerateUnanswered(messages);

        unanswered.Should().ContainSingle();
        unanswered[0].CallIndex.Should().Be(1);
        unanswered[0].Call.id.Should().Be("c2");
    }

    [Fact]
    public void ResultsOutsideTheContiguousRun_DoNotCountAsAnswers()
    {
        // 结果出现在下一轮 assistant 之后：不属于该调用的应答段
        var messages = new List<Message>
        {
            Assistant("c1"),
            ToolResult("c1", "user typed this")
        };

        ToolCallPairing.AnyUnanswered(messages).Should().BeFalse();

        var separated = new List<Message>
        {
            Assistant("c1"),
            Assistant(),
            ToolResult("c1")
        };

        // 第二条 assistant 打断了连续段，因此 c1 视为未应答
        ToolCallPairing.EnumerateUnanswered(separated).Should().ContainSingle();
    }

    [Fact]
    public void MissingIds_FallBackToPosition()
    {
        var messages = new List<Message>
        {
            Assistant("", ""),
            ToolResult("")
        };

        var unanswered = ToolCallPairing.EnumerateUnanswered(messages);

        unanswered.Should().ContainSingle();
        unanswered[0].CallIndex.Should().Be(1);
    }

    [Fact]
    public void MessagesWithoutToolCalls_AreIgnored()
    {
        var messages = new List<Message> { User("hi"), Assistant(), ToolResult("stray") };

        ToolCallPairing.EnumerateUnanswered(messages).Should().BeEmpty();
    }
}
