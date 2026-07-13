using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests;

public class GenerationTreeExecutorTests
{
    private readonly GenerationTreeExecutor _executor = new();
    private readonly Mock<IKVDataService> _kvMock = new();
    private readonly Mock<IToolRegistry> _toolRegistryMock = new();
    private readonly Mock<IGenerationNodeSerializer> _serializerMock = new();
    private readonly Mock<ILocService> _locMock = new();

    private PersistentEnv CreatePersistentEnv()
    {
        return new PersistentEnv
        {
            KVData = _kvMock.Object,
            ChatGuid = Guid.NewGuid(),
            AgentGuid = Guid.NewGuid(),
            ToolRegistry = _toolRegistryMock.Object,
            Serializer = _serializerMock.Object,
            LocService = _locMock.Object,
        };
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsSuccess_ReturnsEnv()
    {
        var node = new Mock<IGenerationNode>();
        node.Setup(n => n.Id).Returns("test-node");
        node.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var result = await _executor.ExecuteAsync(node.Object, CreatePersistentEnv());

        result.Should().NotBeNull();
        result.Persistent.Should().NotBeNull();
        result.Transient.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsFailure_ThrowsInvalidOperationException()
    {
        var node = new Mock<IGenerationNode>();
        node.Setup(n => n.Id).Returns("fail-node");
        node.Setup(n => n.Name).Returns("Failure Node");
        node.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("TEST_ERROR", "Something went wrong"));

        var act = async () => await _executor.ExecuteAsync(node.Object, CreatePersistentEnv());

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // NodeResult doesn't carry NodeName/NodeId (they're null), so "?" fallback is used
        ex.Which.Message.Should().Contain("TEST_ERROR");
        ex.Which.Message.Should().Contain("Something went wrong");
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsFailure_WithDetails_IncludesDetails()
    {
        var node = new Mock<IGenerationNode>();
        node.Setup(n => n.Id).Returns("detail-node");
        node.Setup(n => n.Name).Returns("Detail Node");
        node.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("ERR", "msg", details: "stack trace here"));

        var act = async () => await _executor.ExecuteAsync(node.Object, CreatePersistentEnv());

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("stack trace here");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var node = new Mock<IGenerationNode>();
        node.Setup(n => n.ExecuteAsync(It.IsAny<NodeExecutionContext>()))
            .Returns((NodeExecutionContext ctx) =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(NodeResult.SuccessResult());
            });

        var act = async () => await _executor.ExecuteAsync(node.Object, CreatePersistentEnv(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
