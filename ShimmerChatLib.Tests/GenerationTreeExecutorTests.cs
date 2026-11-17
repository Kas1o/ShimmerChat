using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests;

public class GenerationTreeExecutorTests
{
    private readonly GenerationTreeExecutor _executor = new();
    private readonly Mock<IKVDataService> _kvMock = new();
    private readonly Mock<IToolRegistry> _toolRegistryMock = new();
    private readonly Mock<IPreGenerationNodeSerializer> _serializerMock = new();
    private readonly Mock<ILocService> _locMock = new();
    private readonly Mock<IDebugOutputService> _debugOutputMock = new();

    private PersistentEnv CreatePersistentEnv()
    {
        return new PersistentEnv
        {
            KVData = _kvMock.Object,
            ToolRegistry = _toolRegistryMock.Object,
            Serializer = _serializerMock.Object,
            LocService = _locMock.Object,
            DebugOutput = _debugOutputMock.Object,
            Chat = new Chat { Name = "TestChat" },
            Agent = Agent.Create("TestAgent", "")
        };
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsSuccess_ReturnsEnv()
    {
        var node = new Mock<IPreGenerationNode>();
        node.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());
        var root = new PreGenerationNodeWrapper(node.Object, "test-node");

        var result = await _executor.ExecuteAsync(root, CreatePersistentEnv());

        result.Should().NotBeNull();
        result.Persistent.Should().NotBeNull();
        result.Transient.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsFailure_ThrowsInvalidOperationException()
    {
        var node = new Mock<IPreGenerationNode>();
        node.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("TEST_ERROR", "Something went wrong"));
        var root = new PreGenerationNodeWrapper(node.Object, "Failure Node") { Id = "fail-node" };

        var act = async () => await _executor.ExecuteAsync(root, CreatePersistentEnv());

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        // 包装器负责补全 NodeId / NodeName，业务节点不再需要自己维护。
        ex.Which.Message.Should().Contain("TEST_ERROR");
        ex.Which.Message.Should().Contain("Something went wrong");
        ex.Which.Message.Should().Contain("Failure Node");
        ex.Which.Message.Should().Contain("fail-node");
    }

    [Fact]
    public async Task ExecuteAsync_NodeReturnsFailure_WithDetails_IncludesDetails()
    {
        var node = new Mock<IPreGenerationNode>();
        node.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.Failure("ERR", "msg", details: "stack trace here"));
        var root = new PreGenerationNodeWrapper(node.Object, "Detail Node");

        var act = async () => await _executor.ExecuteAsync(root, CreatePersistentEnv());

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("stack trace here");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var node = new Mock<IPreGenerationNode>();
        node.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .Returns((PreNodeExecutionContext ctx) =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(NodeResult.SuccessResult());
            });
        var root = new PreGenerationNodeWrapper(node.Object, "Cancelled Node");

        var act = async () => await _executor.ExecuteAsync(root, CreatePersistentEnv(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
