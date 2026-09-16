using ShimmerChatBuiltin.Mcp;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Mcp;

/// <summary>内存版 KVData，用于目录读写测试。</summary>
internal sealed class InMemoryKvData : IKVDataService
{
    private readonly Dictionary<(string Space, string Key), string> _values = new();

    public string? Read(string spaceId, string key) =>
        _values.TryGetValue((spaceId, key), out var value) ? value : null;

    public void Write(string spaceId, string key, string value) => _values[(spaceId, key)] = value;
}

/// <summary>
/// 本地化桩。两个重载都实现（接口要求），但各返回可区分的标记，
/// 以便确认被测代码实际命中的是位置插值还是命名插值。
/// </summary>
internal sealed class PassthroughLoc : ILocService
{
    public string CurrentCulture => "en-US";
    public IReadOnlyList<string> SupportedCultures => ["en-US"];

    public string this[string key] => key;

    public string Format(string key, params object[] args) =>
        "POSITIONAL(" + key + ":" + string.Join(",", args.Select(a => a?.ToString() ?? "null")) + ")";

    public string Format(string key, params (string name, object value)[] args) =>
        "NAMED(" + key + ":" + string.Join(",", args.Select(a => a.name + "=" + (a.value?.ToString() ?? "null"))) + ")";

    public void SetCulture(string culture) { }
}

public class McpEndpointCatalogTests
{
    [Fact]
    public void Load_ReturnsEmpty_WhenKeyMissing()
    {
        var kv = new InMemoryKvData();
        Assert.Empty(McpEndpointCatalog.Load(kv));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var kv = new InMemoryKvData();
        var original = new McpEndpointConfig
        {
            Id = "filesystem",
            Name = "Filesystem",
            Transport = McpTransportType.Stdio,
            Command = "npx",
            Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "D:\\data"],
            Environment = ["API_KEY=secret"],
            InheritEnvironment = false,
            WorkingDirectory = "D:\\work",
            InitializationTimeoutSeconds = 45,
            ExposeTools = true,
            ToolsEnabledByDefault = false,
            Tools = [new McpNamedToggle { Name = "read_file", Enabled = true }],
            ResourceInjection = McpResourceInjection.Selected,
            Resources = [new McpNamedToggle { Name = "file:///a.md", Enabled = true }],
            ExposeResourceReadTool = true,
            ResourceCharBudget = 12345
        };

        McpEndpointCatalog.Save(kv, [original]);
        var loaded = McpEndpointCatalog.Load(kv);

        var actual = Assert.Single(loaded);
        Assert.Equal("filesystem", actual.Id);
        Assert.Equal("npx", actual.Command);
        Assert.Equal(3, actual.Arguments.Count);
        Assert.False(actual.InheritEnvironment);
        Assert.Equal(45, actual.InitializationTimeoutSeconds);
        Assert.False(actual.ToolsEnabledByDefault);
        Assert.True(actual.ExposeResourceReadTool);
        Assert.Equal(12345, actual.ResourceCharBudget);
        Assert.Equal(McpResourceInjection.Selected, actual.ResourceInjection);
        Assert.Single(actual.Tools);
        Assert.Single(actual.Resources);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsHttpFields()
    {
        var kv = new InMemoryKvData();
        var config = new McpEndpointConfig
        {
            Id = "remote",
            Name = "Remote",
            Transport = McpTransportType.Http,
            Url = "https://example.com/mcp",
            HttpMode = McpHttpMode.StreamableHttp,
            Headers = ["Authorization: Bearer token"],
            InitializationTimeoutSeconds = 12
        };

        McpEndpointCatalog.Save(kv, [config]);
        var actual = Assert.Single(McpEndpointCatalog.Load(kv));

        Assert.Equal(McpTransportType.Http, actual.Transport);
        Assert.Equal("https://example.com/mcp", actual.Url);
        Assert.Equal(McpHttpMode.StreamableHttp, actual.HttpMode);
        Assert.Single(actual.Headers);
    }

    [Fact]
    public void Load_ThrowsReportableError_WhenJsonBroken()
    {
        var kv = new InMemoryKvData();
        kv.Write(McpEndpointCatalog.SpaceId, McpEndpointCatalog.Key, "{ not json");

        var ex = Assert.Throws<McpConfigException>(() => McpEndpointCatalog.Load(kv));
        Assert.Contains("not valid JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ThrowsReportableError_WhenJsonIsNullLiteral()
    {
        var kv = new InMemoryKvData();
        kv.Write(McpEndpointCatalog.SpaceId, McpEndpointCatalog.Key, "null");

        Assert.Throws<McpConfigException>(() => McpEndpointCatalog.Load(kv));
    }

    [Theory]
    [InlineData("Filesystem Server", "filesystem_server")]
    [InlineData("my-mcp/tools", "my_mcp_tools")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("***", "server")]
    [InlineData("2024 tools", "s_2024_tools")]
    [InlineData("a  b", "a_b")]
    public void NormalizeId_ProducesIdentifierSafeIds(string input, string expected)
    {
        Assert.Equal(expected, McpEndpointCatalog.NormalizeId(input));
    }

    [Fact]
    public void CreateUniqueId_SuffixesOnCollision()
    {
        var existing = new List<McpEndpointConfig> { new() { Id = "filesystem" } };

        var id = McpEndpointCatalog.CreateUniqueId("filesystem", existing);

        Assert.Equal("filesystem_2", id);
    }

    [Fact]
    public void CreateUniqueId_FallsBackToBase_WhenFree()
    {
        var id = McpEndpointCatalog.CreateUniqueId("Filesystem", []);
        Assert.Equal("filesystem", id);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var endpoints = new List<McpEndpointConfig> { new() { Id = "Filesystem" } };
        Assert.NotNull(McpEndpointCatalog.Find(endpoints, "filesystem"));
        Assert.Null(McpEndpointCatalog.Find(endpoints, "other"));
    }

    [Fact]
    public void ParseEnvironment_SplitsOnFirstEquals()
    {
        var parsed = McpEndpointCatalog.ParseEnvironment(["A=1", "B=x=y", "broken", "=2", ""]);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("1", parsed["A"]);
        Assert.Equal("x=y", parsed["B"]);
    }

    [Fact]
    public void ParseHeaders_TrimsNameAndValue()
    {
        var parsed = McpEndpointCatalog.ParseHeaders([" Authorization :  Bearer t ", "broken"]);

        var only = Assert.Single(parsed);
        Assert.Equal("Bearer t", only.Value);
    }

    [Fact]
    public void Clone_IsDeepCopy()
    {
        var source = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Arguments = ["one"],
            Tools = [new McpNamedToggle { Name = "t", Enabled = true }]
        };

        var clone = McpEndpointCatalog.Clone(source);
        clone.Arguments.Add("two");
        clone.Tools[0].Enabled = false;

        Assert.Single(source.Arguments);
        Assert.True(source.Tools[0].Enabled);
    }
}

public class McpEndpointValidationTests
{
    private readonly PassthroughLoc _loc = new();

    [Fact]
    public void Validate_RequiresName()
    {
        var config = new McpEndpointConfig { Id = "a", Command = "npx" };
        var errors = McpEndpointCatalog.Validate(config, _loc);
        Assert.Contains("mcp_err.name_required", errors);
    }

    [Fact]
    public void Validate_RejectsIdWithIllegalCharacters()
    {
        var config = new McpEndpointConfig { Id = "bad-id", Name = "x", Command = "npx" };
        var errors = McpEndpointCatalog.Validate(config, _loc);
        Assert.Contains("mcp_err.id_charset", errors);
    }

    [Fact]
    public void Validate_Stdio_RequiresCommandAndPositiveTimeout()
    {
        var config = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Transport = McpTransportType.Stdio,
            InitializationTimeoutSeconds = 0
        };

        var errors = McpEndpointCatalog.Validate(config, _loc);

        Assert.Contains("mcp_err.command_required", errors);
        Assert.Contains("mcp_err.timeout_positive", errors);
    }

    [Fact]
    public void Validate_Stdio_ReportsMalformedEnvironment()
    {
        var config = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Command = "npx",
            Environment = ["GOOD=1", "BAD"]
        };

        var errors = McpEndpointCatalog.Validate(config, _loc);

        var message = Assert.Single(errors);
        // 确认命中位置插值重载，并且出错的原始行被原样带出（生产实现为 string.Format）。
        Assert.StartsWith("POSITIONAL(mcp_err.env_format:", message, StringComparison.Ordinal);
        Assert.Contains("BAD", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Validate_Http_RejectsNonAbsoluteHttpUrls(string url)
    {
        var config = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Transport = McpTransportType.Http,
            Url = url
        };

        var errors = McpEndpointCatalog.Validate(config, _loc);

        Assert.Contains(errors, e => e.Contains("mcp_err.url", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Http_AcceptsHttpsUrl()
    {
        var config = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Transport = McpTransportType.Http,
            Url = "https://example.com/mcp"
        };

        Assert.Empty(McpEndpointCatalog.Validate(config, _loc));
    }

    [Fact]
    public void Validate_ReportsNegativeBudget()
    {
        var config = new McpEndpointConfig
        {
            Id = "a",
            Name = "A",
            Command = "npx",
            ResourceCharBudget = -1
        };

        Assert.Contains("mcp_err.budget_negative", McpEndpointCatalog.Validate(config, _loc));
    }
}

public class McpToolToggleTests
{
    private static readonly string[] Available = ["read", "write", "list"];

    [Fact]
    public void ToolsEnabledByDefault_ExposesAllUnknownTools()
    {
        var config = new McpEndpointConfig { ToolsEnabledByDefault = true };

        Assert.Equal(Available, config.ResolveEnabledTools(Available));
    }

    [Fact]
    public void ToolsEnabledByDefault_RespectsExplicitDisable()
    {
        var config = new McpEndpointConfig { ToolsEnabledByDefault = true };
        config.SetToolEnabled("write", false);

        Assert.Equal(["read", "list"], config.ResolveEnabledTools(Available));
    }

    [Fact]
    public void ToolsDisabledByDefault_OnlyExposesExplicitlyEnabled()
    {
        var config = new McpEndpointConfig { ToolsEnabledByDefault = false };
        config.SetToolEnabled("list", true);

        Assert.Equal(["list"], config.ResolveEnabledTools(Available));
    }

    [Fact]
    public void IsToolEnabled_FallsBackToDefaultForUnknownTool()
    {
        var config = new McpEndpointConfig { ToolsEnabledByDefault = false };
        Assert.False(config.IsToolEnabled("never-seen"));
    }

    [Fact]
    public void ResourceInjectionOff_ExposesNothing()
    {
        var config = new McpEndpointConfig { ResourceInjection = McpResourceInjection.Off };
        config.SetResourceEnabled("file:///a", true);

        Assert.Equal(["file:///a"], config.ResolveEnabledResources(["file:///a", "file:///b"]));
    }

    [Fact]
    public void ResourceInjectionSelected_OnlyExposesChecked()
    {
        var config = new McpEndpointConfig { ResourceInjection = McpResourceInjection.Selected };
        config.SetResourceEnabled("file:///b", true);

        Assert.Equal(["file:///b"], config.ResolveEnabledResources(["file:///a", "file:///b"]));
    }

    [Fact]
    public void ResourceInjectionAll_ExposesEverythingExceptExplicitOff()
    {
        var config = new McpEndpointConfig { ResourceInjection = McpResourceInjection.All };
        config.SetResourceEnabled("file:///a", false);

        Assert.Equal(["file:///b"], config.ResolveEnabledResources(["file:///a", "file:///b"]));
    }

    [Fact]
    public void SetToggle_DoesNotDuplicateEntries()
    {
        var config = new McpEndpointConfig();
        config.SetToolEnabled("read", true);
        config.SetToolEnabled("read", false);

        var toggle = Assert.Single(config.Tools);
        Assert.False(toggle.Enabled);
    }
}
