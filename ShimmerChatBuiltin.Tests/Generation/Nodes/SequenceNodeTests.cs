namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class SequenceNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_EmptyChildren_ReturnsSuccess()
    {
        var node = new SequenceNode { Nodes = new List<IGenerationNode>() };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_SingleChild_Succeeds()
    {
        var child = new Mock<IGenerationNode>();
        child.Setup(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode { Nodes = new List<IGenerationNode> { child.Object } };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        child.Verify(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Execute_MultipleChildren_ExecutesAllInOrder()
    {
        var order = new List<int>();
        var child1 = new Mock<IGenerationNode>();
        child1.Setup(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .Callback(() => order.Add(1))
            .ReturnsAsync(NodeResult.SuccessResult());
        var child2 = new Mock<IGenerationNode>();
        child2.Setup(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .Callback(() => order.Add(2))
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode
        {
            Nodes = new List<IGenerationNode> { child1.Object, child2.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        order.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Execute_ChildFails_StopsAndReturnsFailure()
    {
        var child1 = new Mock<IGenerationNode>();
        child1.Setup(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("ERR", "child1 failed"));
        var child2 = new Mock<IGenerationNode>();

        var node = new SequenceNode
        {
            Nodes = new List<IGenerationNode> { child1.Object, child2.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be("ERR");
        child2.Verify(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Repeat2_RunsTwice()
    {
        var callCount = 0;
        var child = new Mock<IGenerationNode>();
        child.Setup(c => c.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .Callback(() => callCount++)
            .ReturnsAsync(NodeResult.SuccessResult());

        var node = new SequenceNode
        {
            Repeat = 2,
            Nodes = new List<IGenerationNode> { child.Object }
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeTrue();
        callCount.Should().Be(2);
    }
}
