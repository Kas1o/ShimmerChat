using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin.Mcp;

namespace ShimmerChatBuiltin.Tests.Mcp;

public class McpToolSchemaMapperTests
{
    private static McpToolDescriptor Descriptor(string? schema, string name = "do_thing", string? description = "Does a thing") =>
        new(name, null, description, schema ?? "", null, null);

    [Fact]
    public void ToTool_MapsNameAndDescription()
    {
        var tool = McpToolSchemaMapper.ToTool(Descriptor("""{"type":"object","properties":{}}"""), "mcp_x_do_thing");

        Assert.Equal("mcp_x_do_thing", tool.name);
        Assert.Contains("Does a thing", tool.description);
    }

    [Fact]
    public void ToTool_AppendsTitleWhenDistinct()
    {
        var descriptor = new McpToolDescriptor("t", "Nice Title", "desc", "{}", null, null);

        var tool = McpToolSchemaMapper.ToTool(descriptor, "mcp_x_t");

        Assert.Contains("Nice Title", tool.description);
    }

    [Fact]
    public void ToTool_DoesNotDuplicateTitleEqualToDescription()
    {
        var descriptor = new McpToolDescriptor("t", "same", "same", "{}", null, null);

        var tool = McpToolSchemaMapper.ToTool(descriptor, "mcp_x_t");

        Assert.Equal("same", tool.description);
    }

    [Theory]
    [InlineData("string", ParameterType.String)]
    [InlineData("number", ParameterType.Number)]
    [InlineData("integer", ParameterType.Number)]
    [InlineData("boolean", ParameterType.Boolean)]
    [InlineData("array", ParameterType.Array)]
    [InlineData("object", ParameterType.Object)]
    [InlineData("weird", ParameterType.String)]
    public void BuildParameters_MapsJsonTypes(string jsonType, ParameterType expected)
    {
        var schema = "{\"type\":\"object\",\"properties\":{\"p\":{\"type\":\"" + jsonType + "\",\"description\":\"d\"}}}";

        var parameters = McpToolSchemaMapper.BuildParameters(schema);

        var (parameter, required) = Assert.Single(parameters!);
        Assert.Equal(expected, parameter.type);
        Assert.Equal("p", parameter.name);
        Assert.False(required);
    }

    [Fact]
    public void BuildParameters_MarksRequiredParameters()
    {
        var schema = """
            {"type":"object","properties":{"a":{"type":"string"},"b":{"type":"string"}},"required":["b"]}
            """;

        var parameters = McpToolSchemaMapper.BuildParameters(schema)!;

        Assert.False(parameters.Single(p => p.parameter.name == "a").required);
        Assert.True(parameters.Single(p => p.parameter.name == "b").required);
    }

    [Fact]
    public void BuildParameters_ExtractsEnumValues()
    {
        var schema = """
            {"type":"object","properties":{"mode":{"type":"string","enum":["fast","slow"]}}}
            """;

        var parameters = McpToolSchemaMapper.BuildParameters(schema)!;

        Assert.Equal(["fast", "slow"], parameters[0].parameter.@enum);
    }

    [Fact]
    public void BuildParameters_ResolvesAnyOfNullableType()
    {
        var schema = """
            {"type":"object","properties":{"n":{"anyOf":[{"type":"integer"},{"type":"null"}]}}}
            """;

        var parameters = McpToolSchemaMapper.BuildParameters(schema)!;

        Assert.Equal(ParameterType.Number, parameters[0].parameter.type);
    }

    [Fact]
    public void BuildParameters_EmbedsSchemaForComplexTypes()
    {
        var schema = """
            {"type":"object","properties":{"options":{"type":"object","properties":{"deep":{"type":"string"}}}}}
            """;

        var parameters = McpToolSchemaMapper.BuildParameters(schema)!;

        Assert.Equal(ParameterType.Object, parameters[0].parameter.type);
        Assert.Contains("\"deep\"", parameters[0].parameter.description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"object","properties":{}}""")]
    public void BuildParameters_ReturnsNull_WhenNoUsableParameters(string schema)
    {
        Assert.Null(McpToolSchemaMapper.BuildParameters(schema));
    }

    [Fact]
    public void BuildParameters_SkipsNonObjectPropertyEntries()
    {
        var schema = """
            {"type":"object","properties":{"ok":{"type":"string"},"broken":"nope"}}
            """;

        var parameters = McpToolSchemaMapper.BuildParameters(schema)!;

        Assert.Single(parameters);
        Assert.Equal("ok", parameters[0].parameter.name);
    }
}

public class McpExposedToolNameTests
{
    [Fact]
    public void BuildExposedName_PrefixesEndpointKey()
    {
        Assert.Equal("mcp_filesystem_read_file", McpToolBridge.BuildExposedName("filesystem", "read_file"));
    }

    [Fact]
    public void BuildExposedName_SanitizesCharactersRejectedByLlmApis()
    {
        var name = McpToolBridge.BuildExposedName("srv", "tools/list.files:read");

        Assert.Matches("^[A-Za-z0-9_-]+$", name);
        Assert.StartsWith("mcp_srv_", name, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExposedName_NeverExceedsSixtyFourCharacters()
    {
        var name = McpToolBridge.BuildExposedName(
            new string('e', 40),
            new string('t', 200));

        Assert.True(name.Length <= 64, $"name length was {name.Length}: {name}");
        Assert.Matches("^[A-Za-z0-9_-]+$", name);
    }

    [Fact]
    public void BuildExposedName_KeepsLongNamesDistinct()
    {
        var prefix = new string('t', 120);

        var a = McpToolBridge.BuildExposedName("srv", prefix + "one");
        var b = McpToolBridge.BuildExposedName("srv", prefix + "two");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BuildExposedName_UsesFallbackForSymbolOnlyToolName()
    {
        Assert.Equal("mcp_srv_tool", McpToolBridge.BuildExposedName("srv", "///"));
    }

    [Fact]
    public void BuildExposedName_PrefixesLeadingDigit()
    {
        Assert.Equal("mcp_srv_t1password", McpToolBridge.BuildExposedName("srv", "1password"));
    }
}

public class McpResourceCompositionTests
{
    [Fact]
    public void Compose_ReturnsEmptyForNoBlocks()
    {
        var (text, included, skipped) = McpResources.Compose([], 1000);

        Assert.Equal("", text);
        Assert.Equal(0, included);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void Compose_IncludesAllWhenUnderBudget()
    {
        var blocks = new List<(string, string)> { ("a", "AAA"), ("b", "BBB") };

        var (text, included, skipped) = McpResources.Compose(blocks, 1000);

        Assert.Contains("AAA", text);
        Assert.Contains("BBB", text);
        Assert.Equal(2, included);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void Compose_AnnotatesOmittedBlocksWhenOverBudget()
    {
        var blocks = new List<(string, string)> { ("a", new string('A', 50)), ("b", new string('B', 50)) };

        var (text, included, skipped) = McpResources.Compose(blocks, 60);

        Assert.Equal(1, included);
        Assert.Equal(1, skipped);
        Assert.Contains("omitted", text);
        Assert.Contains("b", text);
    }

    [Fact]
    public void Compose_TreatsNonPositiveBudgetAsUnlimited()
    {
        var blocks = new List<(string, string)> { ("a", new string('A', 5000)) };

        var (_, included, skipped) = McpResources.Compose(blocks, 0);

        Assert.Equal(1, included);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void Truncate_LeavesShortContentUntouched()
    {
        Assert.Equal("abc", McpResources.Truncate("abc", 10, "file:///a"));
    }

    [Fact]
    public void Truncate_AnnotatesWhenCut()
    {
        var result = McpResources.Truncate(new string('x', 50), 10, "file:///a");

        Assert.StartsWith(new string('x', 10), result, StringComparison.Ordinal);
        Assert.Contains("truncated", result);
        Assert.Contains("file:///a", result);
    }

    [Fact]
    public void Render_IncludesEndpointAndUri()
    {
        var rendered = McpResources.Render("Filesystem", "filesystem", "file:///a.md", "a.md", "content");

        Assert.Contains("a.md", rendered);
        Assert.Contains("filesystem", rendered);
        Assert.Contains("content", rendered);
    }
}
