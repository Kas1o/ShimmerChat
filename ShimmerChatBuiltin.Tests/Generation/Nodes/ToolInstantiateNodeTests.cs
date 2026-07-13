namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class ToolInstantiateNodeTests : NodeTestBase
{
    private PersistentEnv CreateEnvWithRegistry(StubToolRegistry registry)
    {
        return new PersistentEnv
        {
            KVData = KvMock.Object,
            ChatGuid = Guid.NewGuid(),
            AgentGuid = Guid.NewGuid(),
            ToolRegistry = registry,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
        };
    }

    [Fact]
    public async Task EmptyTypeName_ReturnsFailure()
    {
        var node = new ToolInstantiateNode { ToolTypeName = "" };
        var env = new GenerationEnv(CreateEnvWithRegistry(new StubToolRegistry()));
        var result = await node.ExecuteAsync(new NodeExecutionContext(env));
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ToolNotFound);
    }

    [Fact]
    public async Task ValidType_CreatesAndAdds()
    {
        var stubTool = new StubAutoCreateToolV2("valid_tool");
        var registry = new StubToolRegistry()
            .SetInstance("Valid.Tool.Type", stubTool);
        var node = new ToolInstantiateNode { ToolTypeName = "Valid.Tool.Type" };
        var env = new GenerationEnv(CreateEnvWithRegistry(registry));
        var ctx = new NodeExecutionContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Tools.Should().HaveCount(1);
        ctx.Env.Transient.Tools[0].Should().Be(stubTool);
    }

    [Fact]
    public async Task InvalidType_ReturnsFailure()
    {
        var registry = new StubToolRegistry();
        var node = new ToolInstantiateNode { ToolTypeName = "Bad.Type" };
        var env = new GenerationEnv(CreateEnvWithRegistry(registry));

        var result = await node.ExecuteAsync(new NodeExecutionContext(env));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ToolNotFound);
    }

    [Fact]
    public async Task CreateReturnsNull_ReturnsFailure()
    {
        var registry = new StubToolRegistry();
        var node = new ToolInstantiateNode { ToolTypeName = "Null.Type" };
        var env = new GenerationEnv(CreateEnvWithRegistry(registry));

        var result = await node.ExecuteAsync(new NodeExecutionContext(env));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ToolNotFound);
    }
}
