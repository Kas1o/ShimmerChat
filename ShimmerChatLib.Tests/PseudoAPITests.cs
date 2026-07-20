using FluentAssertions;
using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

public class PseudoChatCompletionClientTests
{
    private readonly PseudoChatCompletionClient _api = new();

    [Fact]
    public async Task GenerateAsync_ReturnsResponseEx()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "hello world" }, PromptBuilder.From.user),
            }
        };

        var result = await _api.GenerateAsync(pb);

        result.Body.Content.Should().Be("hello world");
        result.FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public async Task GenerateAsync_EmptyMessages()
    {
        var pb = new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

        var result = await _api.GenerateAsync(pb);
        result.Body.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateStreamAsync_ReturnsResponseEx()
    {
        var pb = new PromptBuilder
        {
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "test prompt" }, PromptBuilder.From.user),
            }
        };

        var results = new List<ResponseEx>();
        await foreach (var response in _api.GenerateStreamAsync(pb, CancellationToken.None))
        {
            results.Add(response);
        }

        results.Should().ContainSingle();
        results[0].Body.Content.Should().Be("test prompt");
        results[0].FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public async Task GenerateStreamAsync_EmptyMessages()
    {
        var pb = new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

        var results = new List<ResponseEx>();
        await foreach (var response in _api.GenerateStreamAsync(pb, CancellationToken.None))
        {
            results.Add(response);
        }

        results.Should().ContainSingle();
        results[0].Body.Content.Should().BeEmpty();
    }
}
