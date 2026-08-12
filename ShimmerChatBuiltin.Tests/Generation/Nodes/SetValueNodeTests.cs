using ShimmerChatBuiltin.NodeBasic.PreGeneration;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class SetValueNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_SharedState_String_StoresString()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.SharedState,
            Key = "nickname",
            Value = "Alice",
            ValueType = SetValueType.String
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.SharedState["nickname"].Should().Be("Alice");
    }

    [Fact]
    public async Task Execute_SharedState_Int_StoresTypedValue()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.SharedState,
            Key = "count",
            Value = "42",
            ValueType = SetValueType.Int
        };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.SharedState["count"].Should().Be(42);
        ctx.Env.Transient.SharedState["count"].Should().BeOfType<int>();
    }

    [Fact]
    public async Task Execute_SharedState_OverwritesExistingKey()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.SharedState,
            Key = "k",
            Value = "v2",
            ValueType = SetValueType.String
        };
        var ctx = CreateContext();
        ctx.Env.Transient.SharedState["k"] = "v1";

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.SharedState["k"].Should().Be("v2");
    }

    [Theory]
    [InlineData(SetValueType.Int, "abc")]
    [InlineData(SetValueType.Int, "1.5")]
    [InlineData(SetValueType.Float, "not-a-number")]
    [InlineData(SetValueType.Bool, "yes")]
    public async Task Execute_SharedState_InvalidConversion_Fails(SetValueType type, string value)
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.SharedState,
            Key = "bad",
            Value = value,
            ValueType = type
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        ctx.Env.Transient.SharedState.Should().NotContainKey("bad");
    }

    [Fact]
    public async Task Execute_EmptyKey_Fails()
    {
        var node = new SetValueNode { Target = SetValueTarget.SharedState, Key = "   ", Value = "x" };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
    }

    [Fact]
    public async Task Execute_KVData_WritesToKv()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.KVData,
            Key = "key",
            Value = "val",
            KVCollection = "MySpace"
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        KvMock.Verify(x => x.Write("MySpace", "key", "val"), Times.Once);
    }

    [Fact]
    public async Task Execute_KVData_OverwritesExisting()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.KVData,
            Key = "key",
            Value = "v2",
            KVCollection = "S"
        };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        KvMock.Verify(x => x.Write("S", "key", "v2"), Times.Once);
    }

    [Fact]
    public async Task Execute_KVData_EmptyCollection_Fails()
    {
        var node = new SetValueNode
        {
            Target = SetValueTarget.KVData,
            Key = "key",
            Value = "val",
            KVCollection = " "
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        KvMock.Verify(x => x.Write(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
