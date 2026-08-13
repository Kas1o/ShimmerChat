using Newtonsoft.Json;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class ConfigNodeTests : NodeTestBase
{
    private static ConfigNode CreateNode(params ConfigItem[] items)
    {
        return new ConfigNode { ItemsJson = JsonConvert.SerializeObject(items) };
    }

    [Fact]
    public async Task Execute_NoItems_Succeeds()
    {
        var node = CreateNode();
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.SharedState.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_EmptyItemsJson_Succeeds()
    {
        var node = new ConfigNode { ItemsJson = "" };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.SharedState.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_AppliesConfiguredValues_ToSharedState()
    {
        var node = CreateNode(
            new ConfigItem { Name = "昵称", Key = "nickname", ValueType = SetValueType.String, DefaultValue = "Alice", Value = "Bob" },
            new ConfigItem { Name = "次数", Key = "count", ValueType = SetValueType.Int, DefaultValue = "1", Value = "42" },
            new ConfigItem { Name = "比例", Key = "ratio", ValueType = SetValueType.Float, DefaultValue = "0.5", Value = "0.9" },
            new ConfigItem { Name = "启用", Key = "enabled", ValueType = SetValueType.Bool, DefaultValue = "false", Value = "true" });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.SharedState["nickname"].Should().Be("Bob");
        ctx.Env.Transient.SharedState["count"].Should().Be(42).And.BeOfType<int>();
        ctx.Env.Transient.SharedState["ratio"].Should().Be(0.9f).And.BeOfType<float>();
        ctx.Env.Transient.SharedState["enabled"].Should().Be(true).And.BeOfType<bool>();
    }

    [Fact]
    public async Task Execute_EmptyConfiguredValue_UsesDefault()
    {
        var node = CreateNode(
            new ConfigItem { Key = "count", ValueType = SetValueType.Int, DefaultValue = "7", Value = "" });
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.SharedState["count"].Should().Be(7);
    }

    [Fact]
    public async Task Execute_ValueOverridesDefault()
    {
        var node = CreateNode(
            new ConfigItem { Key = "nickname", ValueType = SetValueType.String, DefaultValue = "Alice", Value = "Bob" });
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.SharedState["nickname"].Should().Be("Bob");
    }

    [Fact]
    public async Task Execute_EmptyKey_Fails()
    {
        var node = CreateNode(new ConfigItem { Name = "no key", Key = "   ", ValueType = SetValueType.String });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        ctx.Env.Transient.SharedState.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_InvalidConfiguredValueConversion_Fails()
    {
        var node = CreateNode(new ConfigItem { Key = "bad", ValueType = SetValueType.Int, Value = "abc" });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        ctx.Env.Transient.SharedState.Should().NotContainKey("bad");
    }

    [Fact]
    public async Task Execute_InvalidDefaultValueConversion_Fails()
    {
        var node = CreateNode(new ConfigItem { Key = "bad", ValueType = SetValueType.Int, DefaultValue = "1.5", Value = "" });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
    }

    [Theory]
    [InlineData(SetValueType.Int, "not-a-number")]
    [InlineData(SetValueType.Float, "nope")]
    [InlineData(SetValueType.Bool, "yes")]
    public async Task Execute_InvalidValueTypes_Fail(SetValueType type, string value)
    {
        var node = CreateNode(new ConfigItem { Key = "bad", ValueType = type, Value = value });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
    }

    [Fact]
    public async Task Execute_CorruptItemsJson_Fails()
    {
        var node = new ConfigNode { ItemsJson = "{ not json" };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
    }

    [Fact]
    public async Task Execute_FailureStopsAtFirstInvalidItem_LeavesPriorKeys()
    {
        var node = CreateNode(
            new ConfigItem { Key = "ok", ValueType = SetValueType.String, Value = "fine" },
            new ConfigItem { Key = "bad", ValueType = SetValueType.Int, Value = "abc" });
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        ctx.Env.Transient.SharedState["ok"].Should().Be("fine");
        ctx.Env.Transient.SharedState.Should().NotContainKey("bad");
    }
}
