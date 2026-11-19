using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ShimmerChat.Singletons;
using ShimmerChatBuiltin.Fragments;
using ShimmerChatBuiltin.Misc.Node.PreGeneration;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 节点树包装器序列化契约：名称/展开状态归包装器，迁移旧格式，失败必须显式返回。
/// </summary>
public class NodeTreeSerializationTests
{
    private static PreGenerationNodeSerializer CreateSerializer(params Type[] extraTypes)
    {
        var loader = new Mock<IPluginLoaderService>();
        loader.Setup(l => l.GetImplementingTypes(typeof(IPreGenerationNode)))
            .Returns(new List<Type> { typeof(SequenceNode), typeof(FragmentNode) }
                .Concat(extraTypes).ToList());
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
    public void RoundTrip_PreservesBypassStateOnWrapper()
    {
        var serializer = CreateSerializer();
        var root = new PreGenerationNodeWrapper(new SequenceNode(), "Root") { IsBypassed = true };
        ((SequenceNode)root.Node!).Nodes.Add(new PreGenerationNodeWrapper(new FragmentNode(), "Child"));

        var json = serializer.Serialize(root);
        var obj = JObject.Parse(json);

        // 旁路状态属于包装器，不进入负载；子节点默认不旁路
        obj["IsBypassed"]!.Value<bool>().Should().BeTrue();
        obj["Node"]!["IsBypassed"].Should().BeNull();

        var restored = serializer.Deserialize(json)!;
        restored.IsBypassed.Should().BeTrue();
        var restoredPayload = (SequenceNode)restored.Node!;
        restoredPayload.Nodes[0].IsBypassed.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_JsonWithoutBypassField_DefaultsToNotBypassed()
    {
        // 旁路是新增字段：已保存的树 JSON 里没有 IsBypassed，读取后必须是「未旁路」，
        // 行为与旧版本一致，不需要任何手动迁移。
        var serializer = CreateSerializer();
        var legacy = $$"""
        {
          "Id": "root-1",
          "Name": "Old Tree",
          "IsExpanded": true,
          "Node": {
            "$type": "{{typeof(SequenceNode).FullName}}",
            "Repeat": 1
          }
        }
        """;

        var restored = serializer.Deserialize(legacy)!;

        restored.IsBypassed.Should().BeFalse();
        restored.Name.Should().Be("Old Tree");
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

    [Fact]
    public void RoundTrip_AppendChatMessages_ImageProcessingOptionsArePreserved()
    {
        // 预算处理是逐树配置：序列化往返后必须原样保留，否则用户配置会悄悄丢失
        var serializer = CreateSerializer(typeof(AppendChatMessagesNode));
        var node = new AppendChatMessagesNode
        {
            ImageProcessing = ChatImageRequestProcessing.Budgeted,
            ImagePixelBudget = 1024L * 1024,
            ImageCompression = ImageCompressionAlgorithm.Jpeg,
            ImageQuality = 70
        };
        var root = new PreGenerationNodeWrapper(node, "History");

        var restored = (AppendChatMessagesNode)serializer.Deserialize(serializer.Serialize(root))!.Node!;

        restored.ImageProcessing.Should().Be(ChatImageRequestProcessing.Budgeted);
        restored.ImagePixelBudget.Should().Be(1024L * 1024);
        restored.ImageCompression.Should().Be(ImageCompressionAlgorithm.Jpeg);
        restored.ImageQuality.Should().Be(70);
    }

    [Fact]
    public void Deserialize_LegacyTreeWithoutImageFields_DefaultsToAsStored()
    {
        // 旧版本保存的树没有图像预算字段：读取后必须落到 AsStored 默认，行为与旧版一致
        var serializer = CreateSerializer(typeof(AppendChatMessagesNode));
        var legacy = $$"""
        {
          "Id": "root-1",
          "Name": "Old History",
          "Node": {
            "$type": "{{typeof(AppendChatMessagesNode).FullName}}",
            "DanglingToolCalls": "InjectPlaceholder"
          }
        }
        """;

        var restored = (AppendChatMessagesNode)serializer.Deserialize(legacy)!.Node!;

        restored.ImageProcessing.Should().Be(ChatImageRequestProcessing.AsStored);
        restored.ImagePixelBudget.Should().Be(ImageAttachmentRules.DefaultPixelBudget);
        restored.ImageCompression.Should().Be(ImageCompressionAlgorithm.WebP);
        restored.ImageQuality.Should().Be(ImageAttachmentRules.DefaultCompressionQuality);
    }
}
