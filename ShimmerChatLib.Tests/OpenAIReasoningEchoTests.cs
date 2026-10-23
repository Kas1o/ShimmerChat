using Newtonsoft.Json.Linq;
using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 请求中 reasoning_content 的回传策略：
/// Full（默认，完整回传）/ AfterLastUser（仅最后一条 user 消息之后）/ None（不回传）。
/// </summary>
public class OpenAIReasoningEchoTests
{
    private static List<JObject> BuildMessages(ReasoningEchoMode? mode = null)
    {
        var client = new OpenAIChatCompletionClient("http://localhost/v1", "key", "model");
        if (mode != null)
            client.ReasoningEchoMode = mode.Value;

        var messages = new (ChatMessage, PromptBuilder.From)[]
        {
            (new ChatMessage { Content = "first answer", thinking = "reasoning-1" }, PromptBuilder.From.assistant),
            (new ChatMessage { Content = "second question" }, PromptBuilder.From.user),
            (new ChatMessage { Content = "second answer", thinking = "reasoning-2" }, PromptBuilder.From.assistant)
        };

        return client.BuildMessages(messages)
            .Select(m => JObject.FromObject((object)m))
            .ToList();
    }

    [Fact]
    public void DefaultMode_IsFullEcho()
    {
        var json = BuildMessages();

        json[0]["reasoning_content"]!.ToString().Should().Be("reasoning-1");
        json[2]["reasoning_content"]!.ToString().Should().Be("reasoning-2");
    }

    [Fact]
    public void Full_EchoesEveryAssistantMessage()
    {
        var json = BuildMessages(ReasoningEchoMode.Full);

        json[0]["reasoning_content"]!.ToString().Should().Be("reasoning-1");
        json[2]["reasoning_content"]!.ToString().Should().Be("reasoning-2");
    }

    [Fact]
    public void AfterLastUser_OnlyEchoesTailAssistantMessages()
    {
        var json = BuildMessages(ReasoningEchoMode.AfterLastUser);

        json[0]["reasoning_content"].Should().BeNull();
        json[2]["reasoning_content"]!.ToString().Should().Be("reasoning-2");
    }

    [Fact]
    public void None_NeverEchoesReasoning()
    {
        var json = BuildMessages(ReasoningEchoMode.None);

        json[0]["reasoning_content"].Should().BeNull();
        json[2]["reasoning_content"].Should().BeNull();
    }
}
