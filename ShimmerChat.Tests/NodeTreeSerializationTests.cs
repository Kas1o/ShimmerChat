using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ShimmerChat.Singletons;
using ShimmerChatBuiltin.Fragments;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 节点树包装器序列化契约：名称/展开状态归包装器，迁移旧格式，失败必须显式返回。
/// </summary>
public class NodeTreeSerializationTests
{
    private static PreGenerationNodeSerializer CreateSerializer()
    {
        var loader = new Mock<IPluginLoaderService>();
        loader.Setup(l => l.GetImplementingTypes(typeof(IPreGenerationNode)))
            .Returns(new List<Type> { typeof(SequenceNode), typeof(FragmentNode) });
        return new PreGenerationNodeSerializer(
            loader.Object, NullLogger<PreGenerationNodeSerializer>.Instance);
    }

    [Fact]
    public void RoundTrip_KeepsNameAndExpansionOutsidePayload()
    {
        var serializer = CreateSerializer();
        var root = new PreGenerationNodeWrapper(new SequenceNode { Repeat = 2 }, "Custom Root")
        {
            IsExpanded = false
        };
        ((SequenceNode)root.Node!).Nodes.Add(new PreGenerationNodeWrapper(new FragmentNode { Content = "hello" }, "Child"));

        var json = serializer.Serialize(root);
        var obj = JObject.Parse(json);

        // 名称/Id/展开状态在包装器上，Repeat 仍在业务节点负载里
        obj["Name"]!.Value<string>().Should().Be("Custom Root");
        obj["IsExpanded"]!.Value<bool>().Should().BeFalse();
        obj["Node"].Should().NotBeNull();
        obj["Repeat"].Should().BeNull();
        obj["Node"]!["Repeat"]!.Value<int>().Should().Be(2);

        var restored = serializer.Deserialize(json)!;
        restored.Id.Should().Be(root.Id);
        restored.Name.Should().Be("Custom Root");
        restored.IsExpanded.Should().BeFalse();
        var restoredPayload = ((SequenceNode)restored.Node!);
        restoredPayload.Repeat.Should().Be(2);
        restoredPayload.Nodes.Should().ContainSingle();
        restoredPayload.Nodes[0].Name.Should().Be("Child");
    }

    [Fact]
    public void Deserialize_LegacyTree_MigratesIdNameAndChildren()
    {
        var serializer = CreateSerializer();
        var legacy = $$"""
        {
          "$type": "{{typeof(SequenceNode).FullName}}",
          "Id": "legacy-root",
          "Name": "Legacy Root",
          "Repeat": 3,
          "Nodes": [
            {
              "$type": "{{typeof(FragmentNode).FullName}}",
              "Id": "legacy-child",
              "Name": "Legacy Child",
              "Content": "old content"
            }
          ]
        }
        """;

        var result = serializer.DeserializeTree(legacy);

        result.Success.Should().BeTrue();
        result.Migrated.Should().BeTrue();
        var root = result.Root.Should().BeOfType<PreGenerationNodeWrapper>().Subject;
        root.Id.Should().Be("legacy-root");
        root.Name.Should().Be("Legacy Root");
        root.IsExpanded.Should().BeTrue();
        var rootPayload = (SequenceNode)root.Node!;
        rootPayload.Repeat.Should().Be(3);
        rootPayload.Nodes.Should().ContainSingle();
        rootPayload.Nodes[0].Id.Should().Be("legacy-child");
        rootPayload.Nodes[0].Name.Should().Be("Legacy Child");
        ((FragmentNode)rootPayload.Nodes[0].Node!).Content.Should().Be("old content");

        // 迁移幂等：新格式再读一次不重复包装，且保留名称/展开状态
        var reround = serializer.DeserializeTree(serializer.Serialize(root));
        reround.Migrated.Should().BeFalse();
        reround.Root!.Name.Should().Be("Legacy Root");
    }

    [Fact]
    public void Deserialize_BrokenJson_ReturnsExplicitFailure()
    {
        var serializer = CreateSerializer();

        var result = serializer.DeserializeTree("{ this is not json");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        serializer.Deserialize("{ this is not json").Should().BeNull();
    }

    [Fact]
    public void Deserialize_UnknownNodeType_ReturnsExplicitFailure()
    {
        var serializer = CreateSerializer();
        var legacy = """
        {
          "$type": "Unknown.Namespace.MissingNode",
          "Id": "x",
          "Name": "Unknown"
        }
        """;

        var result = serializer.DeserializeTree(legacy);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown.Namespace.MissingNode");
    }

    [Fact]
    public void Deserialize_EmptyJson_IsEmptyNotFailure()
    {
        var serializer = CreateSerializer();

        var result = serializer.DeserializeTree("{}");

        result.Success.Should().BeTrue();
        result.IsEmpty.Should().BeTrue();
        result.Root.Should().BeNull();
    }
}
