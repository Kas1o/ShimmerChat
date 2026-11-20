using Newtonsoft.Json;
using SharperLLM.API;
using ShimmerChatBuiltin;

namespace ShimmerChatBuiltin.Tests.API;

/// <summary>
/// Gemini 配置装配：思考档位字面直译、自定义体/头沿用 OpenAI 的显式失败语义、
/// 克隆与 Newtonsoft 往返保持旧配置兼容（枚举按序号持久化，Gemini 必须是末尾追加值）。
/// </summary>
public class GeminiApiConfigTests
{
    private static GeminiChatCompletionClient ResolveClient(ApiConfig config, Action<string>? log = null)
    {
        var setting = config.ToAPISetting(log: log);
        return setting.ChatClient.Should().BeOfType<GeminiChatCompletionClient>().Subject;
    }

    [Fact]
    public void Gemini_BuildsNativeClientWithDefaults()
    {
        var config = new ApiConfig { Type = ApiConfigType.Gemini, GeminiApiKey = "key" };

        var setting = config.ToAPISetting();

        var client = setting.ChatClient.Should().BeOfType<GeminiChatCompletionClient>().Subject;
        client.url.Should().Be(ApiConfig.GeminiDefaultUrl);
        client.model.Should().Be("gemini-4-hydrogen");
        client.maxTokens.Should().Be(16384);
        client.ThinkingLevel.Should().BeNull();
        client.IncludeThoughts.Should().BeTrue();
        client.MidSystemTemplate.Should().BeNull();
        setting.SupportsStreaming.Should().BeTrue();
        setting.SupportsToolCalling.Should().BeTrue();
    }

    [Theory]
    [InlineData(GeminiThinkingLevel.Default, null)]
    [InlineData(GeminiThinkingLevel.Minimal, "minimal")]
    [InlineData(GeminiThinkingLevel.Low, "low")]
    [InlineData(GeminiThinkingLevel.Medium, "medium")]
    [InlineData(GeminiThinkingLevel.High, "high")]
    public void Gemini_ThinkingLevel_MapsLiterally(GeminiThinkingLevel level, string? expected)
    {
        // 字面直译、不做模型名探测：非法组合由 Gemini API 显式报错
        var config = new ApiConfig { Type = ApiConfigType.Gemini, GeminiThinking = level };

        ResolveClient(config).ThinkingLevel.Should().Be(expected);
    }

    [Fact]
    public void Gemini_IncludeThoughts_MapsThrough()
    {
        // 推理摘要默认开；关闭是用户显式选择（字面直译，不与档位联动）
        var config = new ApiConfig { Type = ApiConfigType.Gemini, GeminiIncludeThoughts = false };

        ResolveClient(config).IncludeThoughts.Should().BeFalse();
    }

    [Fact]
    public void Gemini_MidSystemTemplate_PassesThrough()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiMidSystemTemplate = "[System note: {0}]"
        };

        ResolveClient(config).MidSystemTemplate.Should().Be("[System note: {0}]");
    }

    [Fact]
    public void Gemini_CustomRequestBody_MergesIntoRootProperties()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiCustomRequestBody = "{\"safetySettings\": []}"
        };

        var client = ResolveClient(config);

        client.CustomRequestProperties.Should().ContainKey("safetySettings");
    }

    [Fact]
    public void Gemini_CustomRequestBody_InvalidJson_Fails()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiCustomRequestBody = "{ not json"
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Gemini_CustomHeaders_ParsedPerLine()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiCustomHeaders = "Authorization: Bearer tok\nX-Tenant: main"
        };

        var client = ResolveClient(config);

        client.CustomHeaders.Should().NotBeNull();
        client.CustomHeaders!["Authorization"].Should().Be("Bearer tok");
        // 大小写不敏感：同名覆盖语义要可预测
        client.CustomHeaders!["x-tenant"].Should().Be("main");
    }

    [Fact]
    public void Gemini_CustomHeaders_InvalidLine_Fails()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiCustomHeaders = "garbage line without separator"
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Name: Value*");
    }

    [Fact]
    public void Gemini_CustomHeaders_EmptyValue_Fails()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiCustomHeaders = "Authorization:"
        };

        var act = () => config.ToAPISetting();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty name or value*");
    }

    [Fact]
    public void Gemini_Log_IsWiredThrough()
    {
        var config = new ApiConfig { Type = ApiConfigType.Gemini, GeminiApiKey = "key" };

        // 未传 log：客户端内部降级不留痕（AutoChatNameNode 等静态上下文）
        ResolveClient(config).Log.Should().BeNull();
        // 传了 log：垫 dummy 签名等可见事件有出口（APISelectNode 接调试面板）
        ResolveClient(config, _ => { }).Log.Should().NotBeNull();
    }

    [Fact]
    public void Gemini_CloneCopiesAllSettings()
    {
        var config = new ApiConfig
        {
            Name = "Gem",
            Type = ApiConfigType.Gemini,
            GeminiUrl = "https://proxy.example/v1beta",
            GeminiApiKey = "key",
            GeminiModel = "gemini-4-neon",
            GeminiStream = false,
            GeminiCtx = 4096,
            GeminiThinking = GeminiThinkingLevel.High,
            GeminiIncludeThoughts = false,
            GeminiMidSystemTemplate = "[note: {0}]",
            GeminiCustomRequestBody = "{}",
            GeminiCustomHeaders = "Authorization: Bearer x"
        };

        var clone = config.Clone();

        clone.Id.Should().NotBe(config.Id);
        clone.Type.Should().Be(ApiConfigType.Gemini);
        clone.GeminiUrl.Should().Be(config.GeminiUrl);
        clone.GeminiApiKey.Should().Be("key");
        clone.GeminiModel.Should().Be("gemini-4-neon");
        clone.GeminiStream.Should().BeFalse();
        clone.GeminiCtx.Should().Be(4096);
        clone.GeminiThinking.Should().Be(GeminiThinkingLevel.High);
        clone.GeminiIncludeThoughts.Should().BeFalse();
        clone.GeminiMidSystemTemplate.Should().Be("[note: {0}]");
        clone.GeminiCustomRequestBody.Should().Be("{}");
        clone.GeminiCustomHeaders.Should().Be("Authorization: Bearer x");
    }

    [Fact]
    public void Gemini_EnableStreamAndModelName_FollowType()
    {
        var config = new ApiConfig
        {
            Type = ApiConfigType.Gemini,
            GeminiStream = false,
            GeminiModel = "gemini-4-neon"
        };

        config.EnableStream.Should().BeFalse();
        config.ModelName.Should().Be("gemini-4-neon");

        config.GeminiStream = true;
        config.EnableStream.Should().BeTrue();
    }

    [Fact]
    public void Gemini_SurvivesNewtonsoftRoundTrip()
    {
        var configs = new List<ApiConfig>
        {
            new()
            {
                Name = "Gemini",
                Type = ApiConfigType.Gemini,
                GeminiApiKey = "k",
                GeminiModel = "gemini-4-neon",
                GeminiThinking = GeminiThinkingLevel.Medium,
                GeminiIncludeThoughts = false
            }
        };

        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>(JsonConvert.SerializeObject(configs));

        restored![0].Type.Should().Be(ApiConfigType.Gemini);
        restored[0].GeminiModel.Should().Be("gemini-4-neon");
        restored[0].GeminiThinking.Should().Be(GeminiThinkingLevel.Medium);
        restored[0].GeminiIncludeThoughts.Should().BeFalse();
    }

    [Fact]
    public void Gemini_IsTheLastEnumValue_ForNumericPersistence()
    {
        // KVData 用 Newtonsoft 序列化，Type 存成序号；Gemini 必须是末尾追加值，
        // 否则旧配置里的序号会静默错位成别的类型。
        var restored = JsonConvert.DeserializeObject<List<ApiConfig>>("[{\"Type\":7}]");

        restored![0].Type.Should().Be(ApiConfigType.Gemini);
        Enum.GetValues<ApiConfigType>().Last().Should().Be(ApiConfigType.Gemini);
    }
}
