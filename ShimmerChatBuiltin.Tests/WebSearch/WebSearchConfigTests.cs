using ShimmerChatBuiltin.WebSearch;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>
/// 联网搜索配置的读写与校验：损坏数据 / 非法取值必须报错，不静默回落成默认值。
/// </summary>
public class WebSearchConfigTests
{
    private readonly Mock<IKVDataService> _kv = new();

    private void Setup(string? json) =>
        _kv.Setup(k => k.Read("WebSearch", "Config")).Returns(json);

    [Fact]
    public void MissingConfig_ReturnsDefaults()
    {
        Setup(null);

        var (config, error) = WebSearchConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config.Should().NotBeNull();
        config!.ActiveProvider.Should().Be(WebSearchProviderKind.Tavily);
        config.MaxResults.Should().Be(5);
        config.TimeoutSeconds.Should().Be(30);
        config.GetProviderSettings(WebSearchProviderKind.Exa).ApiKey.Should().BeEmpty();
    }

    [Fact]
    public void BrokenJson_ReturnsError()
    {
        Setup("{ oops");

        var (config, error) = WebSearchConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void FromSettingsActiveProvider_IsRejected()
    {
        Setup(WebSearchTestData.ConfigJson(WebSearchProviderKind.FromSettings));

        var (config, error) = WebSearchConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("ActiveProvider");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void OutOfRangeTimeout_IsRejected(int seconds)
    {
        var json = WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily);
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<WebSearchConfig>(json)!;
        config.TimeoutSeconds = seconds;
        Setup(Newtonsoft.Json.JsonConvert.SerializeObject(config));

        var (loaded, error) = WebSearchConfigManager.Load(_kv.Object);

        loaded.Should().BeNull();
        error.Should().Contain("TimeoutSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void OutOfRangeMaxResults_IsRejected(int maxResults)
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<WebSearchConfig>(
            WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily))!;
        config.MaxResults = maxResults;
        Setup(Newtonsoft.Json.JsonConvert.SerializeObject(config));

        var (loaded, error) = WebSearchConfigManager.Load(_kv.Object);

        loaded.Should().BeNull();
        error.Should().Contain("MaxResults");
    }

    [Fact]
    public void NonAbsoluteBaseUrl_IsRejected()
    {
        var config = new WebSearchConfig { ActiveProvider = WebSearchProviderKind.Exa };
        config.Providers[WebSearchProviderKind.Exa] = new WebSearchProviderSettings
        {
            ApiKey = "exa-key",
            BaseUrl = "not-a-url"
        };
        Setup(Newtonsoft.Json.JsonConvert.SerializeObject(config));

        var (loaded, error) = WebSearchConfigManager.Load(_kv.Object);

        loaded.Should().BeNull();
        error.Should().Contain("base URL");
        error.Should().Contain("Exa");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProviderKeysAndBaseUrls()
    {
        string? written = null;
        _kv.Setup(k => k.Write("WebSearch", "Config", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, value) => written = value);

        var config = new WebSearchConfig
        {
            ActiveProvider = WebSearchProviderKind.Exa,
            MaxResults = 8,
            IncludeContent = true,
            Language = "zh",
            Country = "CN"
        };
        config.Providers[WebSearchProviderKind.Exa] = new WebSearchProviderSettings
        {
            ApiKey = "exa-key",
            BaseUrl = "https://proxy.test"
        };

        WebSearchConfigManager.Save(_kv.Object, config);
        Setup(written);

        var (loaded, error) = WebSearchConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        loaded!.ActiveProvider.Should().Be(WebSearchProviderKind.Exa);
        loaded.MaxResults.Should().Be(8);
        loaded.IncludeContent.Should().BeTrue();
        loaded.Language.Should().Be("zh");
        loaded.Country.Should().Be("CN");
        loaded.GetProviderSettings(WebSearchProviderKind.Exa).ApiKey.Should().Be("exa-key");
        loaded.GetProviderSettings(WebSearchProviderKind.Exa).BaseUrl.Should().Be("https://proxy.test");
    }
}
