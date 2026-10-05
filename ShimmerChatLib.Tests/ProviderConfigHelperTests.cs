using Newtonsoft.Json.Linq;

namespace ShimmerChatLib.Tests;

public class ProviderConfigHelperTests
{
    [Fact]
    public void Serialize_OnlyWritesConfiguredProperties()
    {
        var provider = new TestProvider
        {
            Name = "cron",
            Count = 5,
            Ratio = 2.5f,
            Enabled = false,
            Mode = TestMode.B,
            PreTreeJson = "{ \"tree\": 1 }",
            NotConfig = "internal"
        };

        var json = ProviderConfigHelper.Serialize(provider);
        var obj = JObject.Parse(json);

        obj.Properties().Select(p => p.Name).Should().BeEquivalentTo(
            "Name", "Count", "Ratio", "Enabled", "Mode", "PreTreeJson");
        obj["Name"]!.Value<string>().Should().Be("cron");
        obj["Count"]!.Value<int>().Should().Be(5);
        obj["Enabled"]!.Value<bool>().Should().BeFalse();
        // Newtonsoft 默认将枚举序列化为整数
        obj["Mode"]!.Value<int>().Should().Be((int)TestMode.B);
        obj["PreTreeJson"]!.Value<string>().Should().Be("{ \"tree\": 1 }");
    }

    [Fact]
    public void Populate_RoundTripsConfiguredValues()
    {
        var source = new TestProvider
        {
            Name = "nightly",
            Count = 42,
            Ratio = 0.75f,
            Enabled = false,
            Mode = TestMode.B,
            PreTreeJson = "tree-json"
        };
        var json = ProviderConfigHelper.Serialize(source);

        var target = new TestProvider();
        ProviderConfigHelper.Populate(target, json);

        target.Name.Should().Be("nightly");
        target.Count.Should().Be(42);
        target.Ratio.Should().Be(0.75f);
        target.Enabled.Should().BeFalse();
        target.Mode.Should().Be(TestMode.B);
        target.PreTreeJson.Should().Be("tree-json");
        // 非配置属性保持默认值
        target.NotConfig.Should().Be("should-not-change");
    }

    [Fact]
    public void Populate_NullTreeJson_SetsNull()
    {
        var json = ProviderConfigHelper.Serialize(new TestProvider { PreTreeJson = null });

        var target = new TestProvider { PreTreeJson = "stale" };
        ProviderConfigHelper.Populate(target, json);

        target.PreTreeJson.Should().BeNull();
    }

    [Fact]
    public void Populate_IgnoresPropertiesNotMarkedAsConfig()
    {
        // 手工构造的 JSON 里出现非配置属性（例如被篡改的存储数据）不应被写入
        var json = """{ "Name": "x", "NotConfig": "hacked" }""";

        var target = new TestProvider { NotConfig = "should-not-change" };
        ProviderConfigHelper.Populate(target, json);

        target.Name.Should().Be("x");
        target.NotConfig.Should().Be("should-not-change");
    }

    [Fact]
    public void Populate_EmptyOrNullJson_DoesNothing()
    {
        var target = new TestProvider { Name = "keep" };
        ProviderConfigHelper.Populate(target, null);
        ProviderConfigHelper.Populate(target, "");
        ProviderConfigHelper.Populate(target, "  ");
        target.Name.Should().Be("keep");
    }

    [Fact]
    public void Populate_InvalidJson_Throws()
    {
        var act = () => ProviderConfigHelper.Populate(new TestProvider(), "{ not json");
        act.Should().Throw<Newtonsoft.Json.JsonException>();
    }

    [Fact]
    public void GetTreeJson_ReturnsTreeValue()
    {
        var provider = new TestProvider { PreTreeJson = "abc" };
        ProviderConfigHelper.GetTreeJson(provider, nameof(TestProvider.PreTreeJson)).Should().Be("abc");
    }

    [Fact]
    public void GetTreeJson_OnMissingProperty_Throws()
    {
        var act = () => ProviderConfigHelper.GetTreeJson(new TestProvider(), "DoesNotExist");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetTreeJson_OnNonTreeProperty_Throws()
    {
        var act = () => ProviderConfigHelper.GetTreeJson(new TestProvider(), nameof(TestProvider.Name));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetTreeJson_RoundTrip()
    {
        var provider = new TestProvider();
        ProviderConfigHelper.SetTreeJson(provider, nameof(TestProvider.PreTreeJson), "new-tree");
        provider.PreTreeJson.Should().Be("new-tree");

        ProviderConfigHelper.SetTreeJson(provider, nameof(TestProvider.PreTreeJson), null);
        provider.PreTreeJson.Should().BeNull();
    }

    private enum TestMode { A, B }

    private class TestProvider : IGenerationProvider
    {
        [NodeProperty("test.name")]
        public string Name { get; set; } = "default";

        [NodeProperty("test.count")]
        public int Count { get; set; } = 3;

        [NodeProperty("test.ratio")]
        public float Ratio { get; set; } = 1.5f;

        [NodeProperty("test.enabled")]
        public bool Enabled { get; set; } = true;

        [NodeProperty("test.mode")]
        public TestMode Mode { get; set; } = TestMode.A;

        [PipelineTreeProperty("test.pre", ProviderPipelineKind.PreGeneration)]
        public string? PreTreeJson { get; set; }

        // 故意不标记 Attribute：不应被序列化/填充
        public string NotConfig { get; set; } = "should-not-change";

        public Task StartAsync(ProviderRuntimeContext runtime, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;
    }
}
