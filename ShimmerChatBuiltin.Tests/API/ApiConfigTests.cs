using Newtonsoft.Json;
using SharperLLM.API;
using ShimmerChatBuiltin;

namespace ShimmerChatBuiltin.Tests.API;

/// <summary>
/// OpenCode Go 走 OpenAI 兼容路径，但需要自带 User-Agent 与每对话稳定的
/// x-opencode-session 头；模型列表接口与对话接口前缀不同。
/// </summary>
public class ApiConfigTests
{
    private static OpenAIChatCompletionClient ResolveClient(ApiConfig config, string? sessionId)
    {
        var setting = config.ToAPISetting(sessionId);
        return setting.ChatClient.Should().BeOfType<OpenAIChatCompletionClient>().Subject;
    }

    [Fact]
    public void OpenCodeGo_UsesOpenAICompatibleClientWithDefaults()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenCodeGo, OpenCodeGoApiKey = "key" };

        var setting = config.ToAPISetting("chat-1");
        var client = setting.ChatClient.Should().BeOfType<OpenAIChatCompletionClient>().Subject;

        client.url.Should().Be(ApiConfig.OpenCodeGoDefaultUrl);
        client.ModelsUrl.Should().Be(ApiConfig.OpenCodeGoDefaultModelsUrl);
        client.model.Should().Be("glm-5.3-flash");
        setting.SupportsStreaming.Should().BeTrue();
        setting.SupportsToolCalling.Should().BeTrue();
    }

    [Fact]
    public void OpenCodeGo_SendsUserAgentAndSessionHeader()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenCodeGo, OpenCodeGoApiKey = "key" };

        var client = ResolveClient(config, "chat-1");

        client.CustomHeaders.Should().NotBeNull();
        client.CustomHeaders!["User-Agent"].Should().Be(ApiConfig.OpenCodeGoUserAgent);
        client.CustomHeaders!["x-opencode-session"].Should().Be("chat-1");
    }

    [Fact]
    public void OpenCodeGo_WithoutSessionId_OmitsSessionHeader()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenCodeGo, OpenCodeGoApiKey = "key" };

        var client = ResolveClient(config, null);

        client.CustomHeaders.Should().NotBeNull();
        client.CustomHeaders!.Should().ContainKey("User-Agent");
        client.CustomHeaders!.Should().NotContainKey("x-opencode-session");
    }

    [Fact]
    public void OpenCodeGo_RequiresApiKey()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenCodeGo, OpenCodeGoApiKey = null };

        var act = () => config.ToAPISetting("chat-1");

        act.Should().Throw<InvalidOperationException>().WithMessage("*API key*");
    }

    [Fact]
    public void OpenCodeGo_CloneCopiesSettingsAndGetsNewId()
    {
        var config = new ApiConfig
        {
            Name = "Go",
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoUrl = "https://example.test/openai/v1",
            OpenCodeGoModelsUrl = "https://example.test/v1/models",
            OpenCodeGoApiKey = "key",
            OpenCodeGoModel = "kimi-k3",
            OpenCodeGoStream = false,
            OpenCodeGoCtx = 4096,
            OpenCodeGoAsIs = true
        };

        var clone = config.Clone();

        clone.Id.Should().NotBe(config.Id);
        clone.Type.Should().Be(ApiConfigType.OpenCodeGo);
        clone.OpenCodeGoUrl.Should().Be(config.OpenCodeGoUrl);
        clone.OpenCodeGoModelsUrl.Should().Be(config.OpenCodeGoModelsUrl);
        clone.OpenCodeGoApiKey.Should().Be("key");
        clone.OpenCodeGoModel.Should().Be("kimi-k3");
        clone.OpenCodeGoStream.Should().BeFalse();
        clone.OpenCodeGoCtx.Should().Be(4096);
        clone.OpenCodeGoAsIs.Should().BeTrue();
    }

    [Fact]
    public void OpenCodeGo_SurvivesNewtonsoftRoundTrip()
    {
        var configs = new List<ApiConfig>
        {
            new() { Name = "Go", Type = ApiConfigType.OpenCodeGo, OpenCodeGoApiKey = "key", OpenCodeGoModel = "kimi-k3" },
            new() { Name = "OpenAI", Type = ApiConfigType.OpenAI, OpenAIUrl = "http://localhost", OpenAIModel = "gpt" }
        };

        var json = JsonConvert.SerializeObject(configs);
        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>(json);

        restored.Should().NotBeNull();
        var list = restored!;
        list.Select(c => c.Type).Should().Equal(ApiConfigType.OpenCodeGo, ApiConfigType.OpenAI);
        list[0].OpenCodeGoModel.Should().Be("kimi-k3");
    }

    [Fact]
    public void OpenCodeGo_EnableStreamFollowsStreamSetting()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenCodeGo, OpenCodeGoStream = false };

        config.EnableStream.Should().BeFalse();

        config.OpenCodeGoStream = true;
        config.EnableStream.Should().BeTrue();
    }
}
