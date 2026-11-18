using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 包装器旁路契约：IsBypassed 开启时三个管线的包装器都不执行负载、直接返回成功；
/// 容器节点经由包装器入口执行子节点，因此旁路对整棵子树生效。
/// 旁路先于负载检查，损坏节点（负载缺失）可以被临时摘除而不产生执行失败。
/// </summary>
public class NodeWrapperBypassTests
{
    private readonly Mock<IKVDataService> _kvMock = new();
    private readonly Mock<IToolRegistry> _toolRegistryMock = new();
    private readonly Mock<IPreGenerationNodeSerializer> _serializerMock = new();
    private readonly Mock<ILocService> _locMock = new();
    private readonly Mock<IDebugOutputService> _debugOutputMock = new();

    private PersistentEnv CreatePersistentEnv() => new()
    {
        KVData = _kvMock.Object,
        ToolRegistry = _toolRegistryMock.Object,
        Serializer = _serializerMock.Object,
        LocService = _locMock.Object,
        DebugOutput = _debugOutputMock.Object,
        Chat = new Chat { Name = "BypassTestChat" },
        Agent = Agent.Create("BypassTestAgent", "")
    };

    // ─── Pre-Generation ────────────────────────────────

    [Fact]
    public async Task Pre_Bypassed_DoesNotExecutePayload()
    {
        var node = new Mock<IPreGenerationNode>();
        var wrapper = new PreGenerationNodeWrapper(node.Object, "Bypassed") { IsBypassed = true };
        var context = new PreNodeExecutionContext(new PreGenerationEnv(CreatePersistentEnv()));

        var result = await wrapper.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        node.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Pre_Bypassed_MissingPayload_ReturnsSuccessInsteadOfFailure()
    {
        // 插件卸载后残留的损坏节点：旁路后执行不再报 NODE_PAYLOAD_MISSING。
        // 负载为 null 通常由反序列化产生，这里显式构造同样的状态。
        var wrapper = new PreGenerationNodeWrapper(null!, "Broken") { IsBypassed = true };
        var context = new PreNodeExecutionContext(new PreGenerationEnv(CreatePersistentEnv()));

        var result = await wrapper.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Code.Should().BeNull();
    }

    [Fact]
    public async Task Pre_NotBypassed_MissingPayload_StillFailsExplicitly()
    {
        // 对照组：未旁路时损坏负载仍显式失败，旁路不能掩盖数据问题。
        var wrapper = new PreGenerationNodeWrapper(null!, "Broken");
        var context = new PreNodeExecutionContext(new PreGenerationEnv(CreatePersistentEnv()));

        var result = await wrapper.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.PayloadMissing);
    }

    [Fact]
    public async Task Pre_BypassedChild_InContainer_SkipsSubtree()
    {
        // 容器经由包装器入口执行子节点：旁路的子包装器连同其负载一起跳过。
        var skipped = new Mock<IPreGenerationNode>();
        var executed = new Mock<IPreGenerationNode>();
        executed.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());

        var container = new TestSequenceNode(
        [
            new PreGenerationNodeWrapper(skipped.Object, "Skipped") { IsBypassed = true },
            new PreGenerationNodeWrapper(executed.Object, "Executed")
        ]);

        var root = new PreGenerationNodeWrapper(container, "Root");
        var context = new PreNodeExecutionContext(new PreGenerationEnv(CreatePersistentEnv()));

        var result = await root.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        skipped.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Never);
        executed.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Once);
    }

    // ─── Post-Generation ───────────────────────────────

    private PostNodeExecutionContext CreatePostContext()
    {
        var env = new PostGenerationEnv(new ChatMessage { Content = "resp" },
            Array.Empty<ContextSegment>(), CreatePersistentEnv());
        return new PostNodeExecutionContext(env);
    }

    [Fact]
    public async Task Post_Bypassed_DoesNotExecutePayload()
    {
        var node = new Mock<IPostGenerationNode>();
        var wrapper = new PostGenerationNodeWrapper(node.Object, "Bypassed") { IsBypassed = true };

        var result = await wrapper.ExecuteAsync(CreatePostContext());

        result.Success.Should().BeTrue();
        node.Verify(n => n.ExecuteAsync(It.IsAny<PostNodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Post_Bypassed_MissingPayload_ReturnsSuccessInsteadOfFailure()
    {
        var wrapper = new PostGenerationNodeWrapper(null!, "Broken") { IsBypassed = true };

        var result = await wrapper.ExecuteAsync(CreatePostContext());

        result.Success.Should().BeTrue();
        result.Code.Should().BeNull();
    }

    // ─── Render ───────────────────────────────────────

    [Fact]
    public void Render_Bypassed_DoesNotTouchContent()
    {
        var node = new Mock<IRenderModifierNode>();
        var wrapper = new RenderModifierNodeWrapper(node.Object, "Bypassed") { IsBypassed = true };
        var env = new RenderEnv("<p>original</p>",
            Mock.Of<ITreeNodeSerializer>(), _kvMock.Object);

        wrapper.Execute(new RenderNodeExecutionContext(env));

        env.GetContent().Should().Be("<p>original</p>");
        env.ChangeLog.Should().BeEmpty();
        node.Verify(n => n.Execute(It.IsAny<RenderNodeExecutionContext>()), Times.Never);
    }

    [Fact]
    public void Render_Bypassed_MissingPayload_DoesNotThrow()
    {
        var wrapper = new RenderModifierNodeWrapper(null!, "Broken") { IsBypassed = true };
        var env = new RenderEnv("original", Mock.Of<ITreeNodeSerializer>(), _kvMock.Object);

        var act = () => wrapper.Execute(new RenderNodeExecutionContext(env));

        act.Should().NotThrow();
        env.GetContent().Should().Be("original");
    }

    /// <summary>
    /// 与 SequenceNode 同构的最小容器：只经由包装器入口顺序执行子节点。
    /// 用于验证「旁路子包装器 = 跳过该子树」的传播语义。
    /// </summary>
    private sealed class TestSequenceNode(IEnumerable<PreGenerationNodeWrapper> children)
        : IPreGenerationNode
    {
        private readonly List<PreGenerationNodeWrapper> _children = [.. children];

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            foreach (var child in _children)
            {
                var result = await child.ExecuteAsync(context);
                if (!result.Success)
                    return result;
            }
            return NodeResult.SuccessResult();
        }
    }
}
