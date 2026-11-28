using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShimmerChat.Singletons;
using ShimmerChatBuiltin.Misc.Node.Render;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 渲染位置贯穿渲染管线：默认预设只对正文做 Markdown 渲染，思维链原样通过；
/// 自定义树里的节点也能从执行上下文读到位置。
/// </summary>
public class RenderModifierManagerPositionTests
{
    public sealed class CapturingRenderNode : IRenderModifierNode
    {
        public static RenderPosition? LastPosition;

        public void Execute(RenderNodeExecutionContext context)
            => LastPosition = context.Position;
    }

    private static (RenderModifierManager Manager, RenderModifierNodeSerializer Serializer, IKVDataService KV) Build()
    {
        var pluginLoader = new Mock<IPluginLoaderService>();
        pluginLoader
            .Setup(p => p.GetImplementingTypes(typeof(IRenderModifierNode)))
            .Returns([
                typeof(RenderSequenceNode), typeof(RenderCallNode), typeof(RenderIfNode),
                typeof(MarkdownRenderNode), typeof(CapturingRenderNode)
            ]);

        var serializer = new RenderModifierNodeSerializer(
            pluginLoader.Object,
            NullLogger<RenderModifierNodeSerializer>.Instance);

        var store = new Dictionary<(string, string), string>();
        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string space, string key) =>
                store.TryGetValue((space, key), out var value) ? value : null);
        kv.Setup(k => k.Write(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string space, string key, string value) => store[(space, key)] = value);

        var manager = new RenderModifierManager(
            serializer,
            kv.Object,
            Mock.Of<ILocService>(),
            NullLogger<RenderModifierManager>.Instance);

        return (manager, serializer, kv.Object);
    }

    [Fact]
    public void DefaultPreset_MarkdownRendersBodyOnly()
    {
        var (manager, _, _) = Build();

        var (body, bodyLog) = manager.RenderWithLog(null, "**bold**", null,
            allowCacheWrites: true, RenderPosition.Body);
        var (thinking, thinkingLog) = manager.RenderWithLog(null, "**bold**", null,
            allowCacheWrites: true, RenderPosition.Thinking);

        body.Should().Contain("<strong>bold</strong>");
        bodyLog.Should().NotBeEmpty();

        thinking.Should().Be("**bold**");
        thinkingLog.Should().BeEmpty();
    }

    [Fact]
    public void AgentTree_ReceivesRequestedPosition()
    {
        var (manager, serializer, _) = Build();
        var agent = Agent.Create("Agent", "intro");
        agent.RenderModifierTreeJson = serializer.Serialize(
            new RenderModifierNodeWrapper(new CapturingRenderNode(), "root"));

        CapturingRenderNode.LastPosition = null;
        manager.RenderWithLog(agent, "hello", null, allowCacheWrites: true, RenderPosition.Thinking);

        CapturingRenderNode.LastPosition.Should().Be(RenderPosition.Thinking);
    }
}
