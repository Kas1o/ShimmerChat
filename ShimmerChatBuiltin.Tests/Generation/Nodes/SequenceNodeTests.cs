namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class SequenceNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_EmptyChildren_ReturnsSuccess()
    {
        var node = new SequenceNode { Nodes = new List<IPreGenerationNode>() };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_SingleChild_Succeeds()
    {
        var child = new Mock<IPreGenerationNode>();
        child.Setup(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode { Nodes = new List<IPreGenerationNode> { child.Object } };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        child.Verify(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Execute_MultipleChildren_ExecutesAllInOrder()
    {
        var order = new List<int>();
        var child1 = new Mock<IPreGenerationNode>();
        child1.Setup(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .Callback(() => order.Add(1))
            .ReturnsAsync(NodeResult.SuccessResult());
        var child2 = new Mock<IPreGenerationNode>();
        child2.Setup(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .Callback(() => order.Add(2))
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode
        {
            Nodes = new List<IPreGenerationNode> { child1.Object, child2.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        order.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Execute_ChildFails_StopsAndReturnsFailure()
    {
        var child1 = new Mock<IPreGenerationNode>();
        child1.Setup(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("ERR", "child1 failed"));
        var child2 = new Mock<IPreGenerationNode>();

        var node = new SequenceNode
        {
            Nodes = new List<IPreGenerationNode> { child1.Object, child2.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be("ERR");
        child2.Verify(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Repeat2_RunsTwice()
    {
        var callCount = 0;
        var child = new Mock<IPreGenerationNode>();
        child.Setup(c => c.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .Callback(() => callCount++)
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode
        {
            Repeat = 2,
            Nodes = new List<IPreGenerationNode> { child.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        callCount.Should().Be(2);
    }
}
