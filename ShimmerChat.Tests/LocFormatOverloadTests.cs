using ShimmerChat.Singletons;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 记录 <see cref="ILocService.Format(string, object[])"/>（位置插值）与
/// <see cref="ILocService.Format(string, (string, object)[])"/>（命名插值）的解析结果，
/// 用于确认单参数调用实际命中的是哪一个重载。
/// </summary>
public class LocFormatOverloadTests
{
    private sealed class TrackingLoc : ILocService
    {
        public string CurrentCulture => "en-US";
        public IReadOnlyList<string> SupportedCultures => ["en-US"];
        public string this[string key] => key;
        public void SetCulture(string culture) { }

        public string PositionalCalls { get; private set; } = "";
        public string NamedCalls { get; private set; } = "";

        public string Format(string key, params object[] args)
        {
            PositionalCalls = string.Join("|", args.Select(a => a?.ToString() ?? "null"));
            return "positional";
        }

        public string Format(string key, params (string name, object value)[] args)
        {
            NamedCalls = string.Join("|", args.Select(a => $"{a.name}={a.value}"));
            return "named";
        }
    }

    [Fact]
    public void ProductionValidate_UsesPositionalOverload()
    {
        var loc = new TrackingLoc();

        ShimmerChatBuiltin.Mcp.McpEndpointCatalog.Validate(
            new ShimmerChatBuiltin.Mcp.McpEndpointConfig
            {
                Id = "a",
                Name = "A",
                Command = "npx",
                Environment = ["BAD"]
            },
            loc);

        Assert.Equal("BAD", loc.PositionalCalls);
        Assert.Equal("", loc.NamedCalls);
    }

    [Fact]
    public void ExplicitObjectArray_SelectsPositionalOverload()
    {
        var loc = new TrackingLoc();

        var result = loc.Format("some.key", new object[] { "VALUE" });

        Assert.Equal("positional", result);
        Assert.Equal("VALUE", loc.PositionalCalls);
    }
}
