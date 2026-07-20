using ShimmerChatBuiltin.NodeBasic.PreGeneration;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class IfNodeTests : NodeTestBase
{
    [Fact]
    public async Task Match_Is_ExecutesThen()
    {
        var thenNode = new Mock<IPreGenerationNode>();
        thenNode.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());
        var elseNode = new Mock<IPreGenerationNode>();

        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "active";

        var node = new IfNode
        {
            Source = ConditionSource.SharedState,
            SharedStateKey = "status",
            Operator = ConditionOperator.Is,
            Value = "active",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        thenNode.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Once);
        elseNode.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task NoMatch_Is_ExecutesElse()
    {
        var thenNode = new Mock<IPreGenerationNode>();
        var elseNode = new Mock<IPreGenerationNode>();
        elseNode.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["status"] = "inactive";

        var node = new IfNode
        {
            Source = ConditionSource.SharedState,
            SharedStateKey = "status",
            Operator = ConditionOperator.Is,
            Value = "active",
            Then = thenNode.Object,
            Else = elseNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
        thenNode.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Never);
        elseNode.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task NoThenNoElse_ReturnsSuccess()
    {
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["x"] = "1";

        var node = new IfNode
        {
            Source = ConditionSource.SharedState,
            SharedStateKey = "x",
            Operator = ConditionOperator.Is,
            Value = "1",
            Then = null,
            Else = null
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Then_Fails_PropagatesFailure()
    {
        var thenNode = new Mock<IPreGenerationNode>();
        thenNode.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("BRANCH_ERR", "then failed"));

        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["x"] = "1";

        var node = new IfNode
        {
            Source = ConditionSource.SharedState,
            SharedStateKey = "x",
            Operator = ConditionOperator.Is,
            Value = "1",
            Then = thenNode.Object
        };

        var result = await node.ExecuteAsync(CreateContext(env));

        result.Success.Should().BeFalse();
        result.Code.Should().Be("BRANCH_ERR");
    }
}
