using Newtonsoft.Json;
using SharperLLM.API;
using ShimmerChatBuiltin;

namespace ShimmerChatBuiltin.Tests.API;

/// <summary>
/// OpenCode Go 配置装配：OpenAI 兼容（默认）与 Gemini 原生两种模式共用
/// User-Agent 与 x-opencode-session，Gemini 模式改走 Gemini 客户端并用 Bearer 鉴权。
/// </summary>
public class ApiConfigTests
{
    private static OpenAIChatCompletionClient ResolveClient(ApiConfig config, string? sessionId)
    {
        var setting = config.ToAPISetting(sessionId);
        return setting.ChatClient.Should().BeOfType<OpenAIChatCompletionClient>().Subject;
    }

    private static GeminiChatCompletionClient ResolveGeminiClient(ApiConfig config, string? sessionId = null)
    {
        var setting = config.ToAPISetting(sessionId);
        return setting.ChatClient.Should().BeOfType<GeminiChatCompletionClient>().Subject;
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

    [Fact]
    public void OpenCodeGo_GeminiMode_BuildsGeminiClientWithBearerAndDefaults()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoModel = ""
        };

        var setting = config.ToAPISetting("chat-1");
        var client = setting.ChatClient.Should().BeOfType<GeminiChatCompletionClient>().Subject;

        client.url.Should().Be(ApiConfig.OpenCodeGoGeminiDefaultUrl);
        client.apiKey.Should().Be("key");
        client.model.Should().Be(ApiConfig.OpenCodeGoDefaultGeminiModel);
        client.maxTokens.Should().Be(16384);
        // OpenCode Go 的 Google 路径只接受 Bearer，不能发 x-goog-api-key
        client.UseBearerAuth.Should().BeTrue();
        client.ThinkingLevel.Should().BeNull();
        client.IncludeThoughts.Should().BeTrue();
        client.MidSystemTemplate.Should().BeNull();
        setting.SupportsStreaming.Should().BeTrue();
        setting.SupportsToolCalling.Should().BeTrue();
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_KeepsUserAgentAndSessionHeader()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key"
        };

        var client = ResolveGeminiClient(config, "chat-1");

        client.CustomHeaders.Should().NotBeNull();
        client.CustomHeaders!["User-Agent"].Should().Be(ApiConfig.OpenCodeGoUserAgent);
        client.CustomHeaders!["x-opencode-session"].Should().Be("chat-1");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_KeepsExplicitModel()
    {
        // 模型值是用户数据：装配层不做“看起来像另一个模式默认值”的隐式替换，
        // 模式切换时的默认模型调整只发生在设置页的可见交互里。
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoModel = "glm-5.3-flash"
        };

        ResolveGeminiClient(config).model.Should().Be("glm-5.3-flash");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_UsesCustomGeminiUrlWhenSet()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoGeminiUrl = "https://gateway.test/google/v1beta"
        };

        ResolveGeminiClient(config).url.Should().Be("https://gateway.test/google/v1beta");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_MapsThinkingAndThoughts()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoGeminiThinking = GeminiThinkingLevel.High,
            OpenCodeGoGeminiIncludeThoughts = false,
            OpenCodeGoGeminiMidSystemTemplate = "[note: {0}]"
        };

        var client = ResolveGeminiClient(config);

        client.ThinkingLevel.Should().Be("high");
        client.IncludeThoughts.Should().BeFalse();
        client.MidSystemTemplate.Should().Be("[note: {0}]");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_CustomRequestBodyMergesIntoRoot()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoGeminiCustomRequestBody = "{\"safetySettings\": []}"
        };

        ResolveGeminiClient(config).CustomRequestProperties.Should().ContainKey("safetySettings");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_InvalidCustomRequestBodyFails()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = "key",
            OpenCodeGoGeminiCustomRequestBody = "{ not json"
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*OpenCode Go Gemini*not valid JSON*");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_RequiresApiKey()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoApiKey = null
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*API key*");
    }

    [Fact]
    public void OpenCodeGo_GeminiMode_FollowsStreamSetting()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoStream = false
        };

        config.ToAPISetting().SupportsStreaming.Should().BeFalse();
    }

    [Fact]
    public void OpenCodeGo_CloneCopiesGeminiModeSettings()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoMode = OpenCodeGoMode.Gemini,
            OpenCodeGoGeminiUrl = "https://gateway.test/google/v1beta",
            OpenCodeGoGeminiThinking = GeminiThinkingLevel.Low,
            OpenCodeGoGeminiIncludeThoughts = false,
            OpenCodeGoGeminiMidSystemTemplate = "[note: {0}]",
            OpenCodeGoGeminiCustomRequestBody = "{\"safetySettings\": []}"
        };

        var clone = config.Clone();

        clone.OpenCodeGoMode.Should().Be(OpenCodeGoMode.Gemini);
        clone.OpenCodeGoGeminiUrl.Should().Be(config.OpenCodeGoGeminiUrl);
        clone.OpenCodeGoGeminiThinking.Should().Be(GeminiThinkingLevel.Low);
        clone.OpenCodeGoGeminiIncludeThoughts.Should().BeFalse();
        clone.OpenCodeGoGeminiMidSystemTemplate.Should().Be("[note: {0}]");
        clone.OpenCodeGoGeminiCustomRequestBody.Should().Be("{\"safetySettings\": []}");
    }

    [Fact]
    public void OpenCodeGo_GeminiModeSurvivesNewtonsoftRoundTrip()
    {
        var configs = new List<ApiConfig>
        {
            new()
            {
                Type = ApiConfigType.OpenCodeGo,
                OpenCodeGoMode = OpenCodeGoMode.Gemini,
                OpenCodeGoModel = "gemini-4-argon",
                OpenCodeGoGeminiThinking = GeminiThinkingLevel.Medium
            }
        };

        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>(JsonConvert.SerializeObject(configs));

        restored![0].OpenCodeGoMode.Should().Be(OpenCodeGoMode.Gemini);
        restored[0].OpenCodeGoModel.Should().Be("gemini-4-argon");
        restored[0].OpenCodeGoGeminiThinking.Should().Be(GeminiThinkingLevel.Medium);
    }

    [Fact]
    public void OpenCodeGo_OldConfigWithoutMode_DefaultsToOpenAICompatible()
    {
        // 旧配置没有 OpenCodeGoMode：枚举序号 0 必须是 OpenAI 兼容，行为与旧版一致。
        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>("[{\"Type\":6,\"OpenCodeGoApiKey\":\"k\"}]");

        var config = restored![0];
        config.OpenCodeGoMode.Should().Be(OpenCodeGoMode.OpenAICompatible);
        config.ToAPISetting("chat-1").ChatClient.Should().BeOfType<OpenAIChatCompletionClient>();
    }

    [Fact]
    public void OpenCodeGo_UnknownMode_FailsExplicitly()
    {
        // 坏配置不能静默落到 OpenAI 路径：未知模式必须显式失败。
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenCodeGo,
            OpenCodeGoApiKey = "key",
            OpenCodeGoMode = (OpenCodeGoMode)42
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported OpenCode Go mode*");
    }

    [Fact]
    public void OpenAI_ReasoningEchoDefaultsToFull()
    {
        var config = new ApiConfig { Type = ApiConfigType.OpenAI, OpenAIApiKey = "key" };

        ResolveClient(config, null).ReasoningEchoMode.Should().Be(ReasoningEchoMode.Full);
    }

    [Fact]
    public void OpenAI_ReasoningEchoModeIsConfigurable()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenAI,
            OpenAIApiKey = "key",
            OpenAIReasoningEchoMode = ReasoningEchoMode.None
        };

        ResolveClient(config, null).ReasoningEchoMode.Should().Be(ReasoningEchoMode.None);
    }

    [Fact]
    public void DeepSeek_ReasoningEchoIsAlwaysFull()
    {
        var config = new ApiConfig { Type = ApiConfigType.DeepSeek, DeepSeekApiKey = "key" };

        ResolveClient(config, null).ReasoningEchoMode.Should().Be(ReasoningEchoMode.Full);
    }

    [Fact]
    public void Clone_CopiesOpenAIReasoningEchoMode()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.OpenAI,
            OpenAIReasoningEchoMode = ReasoningEchoMode.AfterLastUser
        };

        config.Clone().OpenAIReasoningEchoMode.Should().Be(ReasoningEchoMode.AfterLastUser);
    }

    [Fact]
    public void OpenAI_ReasoningEchoModeSurvivesNewtonsoftRoundTrip()
    {
        var configs = new List<ApiConfig>
        {
            new() { Type = ApiConfigType.OpenAI, OpenAIReasoningEchoMode = ReasoningEchoMode.AfterLastUser }
        };

        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>(JsonConvert.SerializeObject(configs));

        restored![0].OpenAIReasoningEchoMode.Should().Be(ReasoningEchoMode.AfterLastUser);
    }

    [Fact]
    public void OpenAI_MissingReasoningEchoModeDefaultsToFull()
    {
        // 旧配置没有该字段，反序列化后必须落到完整回传。
        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>("[{\"Type\":2}]");

        restored![0].OpenAIReasoningEchoMode.Should().Be(ReasoningEchoMode.Full);
    }

    [Fact]
    public void Pinned_DefaultsToFalseAndSurvivesRoundTrip()
    {
        // 旧配置没有 Pinned 字段时按未收藏处理。
        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>("[{\"Type\":4}]");
        restored![0].Pinned.Should().BeFalse();

        var configs = new List<ApiConfig>
        {
            new() { Name = "Pinned", Type = ApiConfigType.OpenAI, Pinned = true }
        };

        var roundTripped = JsonConvert.DeserializeObject<List<ApiConfig>>(
            JsonConvert.SerializeObject(configs));

        roundTripped![0].Pinned.Should().BeTrue();
    }

    [Fact]
    public void Clone_CopiesPinnedFlag()
    {
        var config = new ApiConfig { Name = "Pinned", Type = ApiConfigType.OpenAI, Pinned = true };

        config.Clone().Pinned.Should().BeTrue();
    }
}
