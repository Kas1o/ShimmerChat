using Newtonsoft.Json.Linq;
using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// OpenAI 兼容 API 的多模态请求构建：纯文本保持字符串 content，
/// 携带图像时改为 content 内容块数组（text + 每个图像一个 image_url）。
/// </summary>
public class OpenAIMultimodalRequestTests
{
    private static JObject BuildMessage(ChatMessage message, PromptBuilder.From from, bool asIs = false)
    {
        var client = new OpenAIChatCompletionClient("http://localhost/v1", "key", "model", _as_is: asIs);
        object built = client.BuildMessages(new[] { (message, from) }).Single();
        return JObject.FromObject(built);
    }

    [Fact]
    public void TextOnlyMessage_KeepsStringContent()
    {
        var json = BuildMessage(new ChatMessage { Content = "hello" }, PromptBuilder.From.user);

        json["role"]!.ToString().Should().Be("user");
        json["content"]!.Type.Should().Be(JTokenType.String);
        json["content"]!.ToString().Should().Be("hello");
    }

    [Fact]
    public void Images_ProduceTextAndImageUrlContentParts()
    {
        var message = new ChatMessage
        {
            Content = "what is this?",
            Images = new List<ChatImage>
            {
                ChatImage.FromBase64("AAA", "image/png"),
                ChatImage.FromBase64("BBB", "image/webp")
            }
        };

        var content = BuildMessage(message, PromptBuilder.From.user)["content"];

        content!.Type.Should().Be(JTokenType.Array);
        var parts = (JArray)content;
        parts.Count.Should().Be(3);
        parts[0]!["type"]!.ToString().Should().Be("text");
        parts[0]!["text"]!.ToString().Should().Be("what is this?");
        parts[1]!["type"]!.ToString().Should().Be("image_url");
        parts[1]!["image_url"]!["url"]!.ToString().Should().Be("data:image/png;base64,AAA");
        parts[2]!["image_url"]!["url"]!.ToString().Should().Be("data:image/webp;base64,BBB");
    }

    [Fact]
    public void ImageWithoutData_FailsRequestBuilding()
    {
        // 图像块必须在进入客户端之前解析好：这里缺数据要明确失败，不能发出空图像
        var message = new ChatMessage
        {
            Content = "",
            Images = new List<ChatImage> { new() { MimeType = "image/png", Base64 = "" } }
        };

        var act = () => BuildMessage(message, PromptBuilder.From.user);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Images_StillCarryThinkingAndCustomProperties()
    {
        var message = new ChatMessage
        {
            Content = "look",
            thinking = "reasoning",
            CustomProperties = new Dictionary<string, object> { ["custom_flag"] = true },
            Images = new List<ChatImage> { ChatImage.FromBase64("AAA", "image/png") }
        };

        var json = BuildMessage(message, PromptBuilder.From.assistant);

        json["content"]!.Type.Should().Be(JTokenType.Array);
        json["reasoning_content"]!.ToString().Should().Be("reasoning");
        json["custom_flag"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task TextCompletionAdapter_RejectsMessagesWithImages()
    {
        var adapter = new TextToChatAdapter(new StubTextCompletionClient(), PromptBuilder.ChatML);
        var prompt = new PromptBuilder
        {
            Messages =
            [
                (new ChatMessage
                {
                    Content = "look",
                    Images = new List<ChatImage> { ChatImage.FromBase64("AAA", "image/png") }
                }, PromptBuilder.From.user)
            ]
        };

        var act = async () => await adapter.GenerateAsync(prompt);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task TextCompletionAdapter_StillWorksForTextOnlyPrompts()
    {
        var adapter = new TextToChatAdapter(new StubTextCompletionClient(), PromptBuilder.ChatML);
        var prompt = new PromptBuilder
        {
            Messages = [(new ChatMessage { Content = "hi" }, PromptBuilder.From.user)]
        };

        var response = await adapter.GenerateAsync(prompt);

        response.Body.Content.Should().Be("stub-response");
    }

    private class StubTextCompletionClient : ITextCompletionClient
    {
        public Task<string> GenerateAsync(string prompt) => Task.FromResult("stub-response");

        public async IAsyncEnumerable<string> GenerateStreamAsync(
            string prompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return await GenerateAsync(prompt);
        }
    }
}
