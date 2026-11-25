using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// RenderCallNode 的预设树反序列化缓存：同一份预设 JSON 只反序列化一次，
/// 预设内容变化后必须重新反序列化；预设列表本身按存储版本缓存。
/// </summary>
public class RenderCallNodeCacheTests
{
    public RenderCallNodeCacheTests()
    {
        RenderCallNode.ClearCacheForTests();
    }

    private sealed class CountingNode : IRenderModifierNode
    {
        public int Calls;

        public void Execute(RenderNodeExecutionContext context)
        {
            Calls++;
        }
    }

    private sealed class FakeKVDataService : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _values = new();

        public string? Read(string spaceId, string key) =>
            _values.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value) =>
            _values[(spaceId, key)] = value;
    }

    private static void SetPreset(IKVDataService kv, string id, string rootNodeJson)
    {
        PipelinePresetStore.WriteAll(kv, GenerationPipeline.RenderModifier,
        [
            new RenderModifierPreset { Id = id, Name = id, RootNodeJson = rootNodeJson }
        ]);
    }

    private static (Mock<ITreeNodeSerializer> Serializer, CountingNode Child, int[] DeserializeCount) BuildSerializer()
    {
        var child = new CountingNode();
        var count = new int[1];
        var serializer = new Mock<ITreeNodeSerializer>();
        serializer
            .Setup(s => s.Deserialize(It.IsAny<string>()))
            .Returns(() =>
            {
                count[0]++;
                return new RenderModifierNodeWrapper(child, "child");
            });

        return (serializer, child, count);
    }

    [Fact]
    public void SamePresetJson_DeserializesOnce()
    {
        var (serializer, child, deserializeCount) = BuildSerializer();
        var kv = new FakeKVDataService();
        SetPreset(kv, "p1", "{\"root\":1}");

        var wrapper = new RenderModifierNodeWrapper(
            new RenderCallNode { PresetId = "p1" }, "call");

        Execute(wrapper, kv, serializer.Object);
        Execute(wrapper, kv, serializer.Object);

        deserializeCount[0].Should().Be(1);
        child.Calls.Should().Be(2);
    }

    [Fact]
    public void ChangedPresetJson_DeserializesAgain()
    {
        var (serializer, _, deserializeCount) = BuildSerializer();
        var kv = new FakeKVDataService();
        SetPreset(kv, "p1", "{\"root\":1}");

        var wrapper = new RenderModifierNodeWrapper(
            new RenderCallNode { PresetId = "p1" }, "call");

        Execute(wrapper, kv, serializer.Object);

        SetPreset(kv, "p1", "{\"root\":2}");
        Execute(wrapper, kv, serializer.Object);

        deserializeCount[0].Should().Be(2);
    }

    private static void Execute(RenderModifierNodeWrapper wrapper, IKVDataService kv,
        ITreeNodeSerializer serializer)
    {
        var env = new RenderEnv("hello", serializer, kv, loc: Mock.Of<ILocService>());
        wrapper.Execute(new RenderNodeExecutionContext(env));
    }
}
