namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class IfNodeTests : NodeTestBase
{
    [Fact]
    public async Task Condition_Match_Equals_ExecutesThen()
    {
        var thenNode = new Mock<IGenerationNode>();
        thenNode.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());
        var elseNode = new Mock<IGenerationNode>();

        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "active";

        var node = new IfNode
        {
            Condition = "SharedState['status'] == \"active\"",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        thenNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Once);
        elseNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Condition_NoMatch_Equals_ExecutesElse()
    {
        var thenNode = new Mock<IGenerationNode>();
        var elseNode = new Mock<IGenerationNode>();
        elseNode.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "inactive";

        var node = new IfNode
        {
            Condition = "SharedState['status'] == \"active\"",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        thenNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Never);
        elseNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Condition_Match_NotEquals_ExecutesThen()
    {
        var thenNode = new Mock<IGenerationNode>();
        thenNode.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());
        var elseNode = new Mock<IGenerationNode>();

        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "inactive";

        var node = new IfNode
        {
            Condition = "SharedState['status'] != \"active\"",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        thenNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Condition_NoMatch_NotEquals_ExecutesElse()
    {
        var thenNode = new Mock<IGenerationNode>();
        var elseNode = new Mock<IGenerationNode>();
        elseNode.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "active";

        var node = new IfNode
        {
            Condition = "SharedState['status'] != \"active\"",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        elseNode.Verify(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Condition_Empty_ReturnsSuccess()
    {
        var node = new IfNode { Condition = "" };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Condition_NoOperator_ReturnsSuccess()
    {
        var node = new IfNode { Condition = "garbage" };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Then_Null_ConditionTrue_ReturnsSuccess()
    {
        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["x"] = "1";

        var node = new IfNode
        {
            Condition = "SharedState['x'] == \"1\"",
            Then = null,
            Else = null
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Else_Null_ConditionFalse_ReturnsSuccess()
    {
        var node = new IfNode
        {
            Condition = "SharedState['x'] == \"1\"",
            Then = null,
            Else = null
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Then_Fails_PropagatesFailure()
    {
        var thenNode = new Mock<IGenerationNode>();
        thenNode.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("BRANCH_ERR", "then failed"));

        var env = new GenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["x"] = "1";

        var node = new IfNode
        {
            Condition = "SharedState['x'] == \"1\"",
            Then = thenNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeFalse();
        result.Code.Should().Be("BRANCH_ERR");
    }
}
