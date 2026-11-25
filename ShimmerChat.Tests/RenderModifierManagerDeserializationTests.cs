using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShimmerChat.Singletons;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// RenderModifierManager 的根树反序列化缓存：同一份 Agent 渲染树 JSON 只构建一次节点树，
/// 树内容变化后必须重新反序列化。
/// </summary>
public class RenderModifierManagerDeserializationTests
{
    public sealed class CountingRenderNode : IRenderModifierNode
    {
        public static int Constructed;

        public CountingRenderNode()
        {
            Constructed++;
        }

        public void Execute(RenderNodeExecutionContext context)
        {
            // 节点本身不改写内容，测试只关心树是否被重新构造。
        }
    }

    private static (RenderModifierManager Manager, RenderModifierNodeSerializer Serializer, Agent Agent) Build()
    {
        var pluginLoader = new Mock<IPluginLoaderService>();
        pluginLoader
            .Setup(p => p.GetImplementingTypes(typeof(IRenderModifierNode)))
            .Returns([typeof(CountingRenderNode)]);

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

        var agent = Agent.Create("Agent", "intro");
        agent.RenderModifierTreeJson = serializer.Serialize(
            new RenderModifierNodeWrapper(new CountingRenderNode(), "root"));

        return (manager, serializer, agent);
    }

    [Fact]
    public void SameTreeJson_DeserializesOnce()
    {
        var (manager, _, agent) = Build();

        CountingRenderNode.Constructed = 0;
        manager.RenderWithLog(agent, "hello");
        manager.RenderWithLog(agent, "hello");

        CountingRenderNode.Constructed.Should().Be(1);
    }

    [Fact]
    public void ChangedTreeJson_DeserializesAgain()
    {
        var (manager, serializer, agent) = Build();

        CountingRenderNode.Constructed = 0;
        manager.RenderWithLog(agent, "hello");
        CountingRenderNode.Constructed.Should().Be(1);

        agent.RenderModifierTreeJson = serializer.Serialize(
            new RenderModifierNodeWrapper(new CountingRenderNode(), "root-changed"));

        CountingRenderNode.Constructed = 0;
        manager.RenderWithLog(agent, "hello");

        CountingRenderNode.Constructed.Should().Be(1);
    }
}
