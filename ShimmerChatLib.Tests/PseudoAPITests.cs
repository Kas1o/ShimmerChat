using FluentAssertions;
using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

public class PseudoAPITests
{
    private readonly PseudoAPI _api = new();

    [Fact]
    public async Task GenerateText_ReturnsPrompt()
    {
        var result = await _api.GenerateText("hello world");
        result.Should().Be("hello world");
    }

    [Fact]
    public async Task GenerateText_EmptyPrompt()
    {
        var result = await _api.GenerateText("");
        result.Should().Be("");
    }

    [Fact]
    public async Task GenerateTextStream_ReturnsPrompt()
    {
        var results = new List<string>();
        await foreach (var chunk in _api.GenerateTextStream("test prompt", CancellationToken.None))
        {
            results.Add(chunk);
        }

        results.Should().ContainSingle().Which.Should().Be("test prompt");
    }

    [Fact]
    public async Task GenerateChatReply_ReturnsLastMessage()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "system" }, PromptBuilder.From.system),
                (new ChatMessage { Content = "user message" }, PromptBuilder.From.user),
            }
        };

        var result = await _api.GenerateChatReply(pb);
        result.Should().Be("user message");
    }

    [Fact]
    public async Task GenerateChatReply_EmptyMessages()
    {
        var pb = new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

        var result = await _api.GenerateChatReply(pb);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateChatReplyStream_ReturnsLastMessage()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "first" }, PromptBuilder.From.user),
                (new ChatMessage { Content = "last" }, PromptBuilder.From.user),
            }
        };

        var results = new List<string>();
        await foreach (var chunk in _api.GenerateChatReplyStream(pb, CancellationToken.None))
        {
            results.Add(chunk);
        }

        results.Should().ContainSingle().Which.Should().Be("last");
    }

    [Fact]
    public async Task GenerateChatEx_ReturnsResponseEx()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "hello" }, PromptBuilder.From.user),
            }
        };

        var result = await _api.GenerateChatEx(pb);

        result.Body.Content.Should().Be("hello");
        result.FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public async Task GenerateChatExStream_ReturnsResponseEx()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "test" }, PromptBuilder.From.user),
            }
        };

        var results = new List<ResponseEx>();
        await foreach (var response in _api.GenerateChatExStream(pb, CancellationToken.None))
        {
            results.Add(response);
        }

        results.Should().ContainSingle();
        results[0].Body.Content.Should().Be("test");
        results[0].FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public async Task GenerateChatExStream_EmptyMessages()
    {
        var pb = new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

        var results = new List<ResponseEx>();
        await foreach (var response in _api.GenerateChatExStream(pb, CancellationToken.None))
        {
            results.Add(response);
        }

        results.Should().ContainSingle();
        results[0].Body.Content.Should().BeEmpty();
    }
}
