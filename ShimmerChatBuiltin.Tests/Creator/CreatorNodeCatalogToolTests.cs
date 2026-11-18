using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.Creator;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Tests.SubAgent;

namespace ShimmerChatBuiltin.Tests.Creator;

/// <summary>
/// creator_node_catalog 的行为契约：目录来自 INodeTypeCatalog（不复制注册表），
/// 属性元数据由 [NodeProperty] 反射生成，枚举给出数字值（树 JSON 的存储格式），
/// 宿主服务缺失时显式失败。
/// </summary>
public class CreatorNodeCatalogToolTests
{
    private static ToolInvocationContext Invocation(string json)
        => CreatorTestDoubles.Invocation("creator_node_catalog", json);

    private CreatorTestDoubles.StubNodeCatalog CatalogWithBuiltinNodes()
    {
        var catalog = new CreatorTestDoubles.StubNodeCatalog();
        catalog.Types.Add(new NodeTypeMetadata(
            "node.sequence", typeof(SequenceNode), "▦", "var(--node-flow)", null, "category.flow"));
        catalog.Types.Add(new NodeTypeMetadata(
            "node.fragment", typeof(FragmentNode), "▤", "var(--node-fragment)",
            "node.fragment.desc", "category.content/category.fragment"));
        catalog.Types.Add(new NodeTypeMetadata(
            "node.post_sequence", typeof(PostSequenceNode), "▦", "var(--node-flow)", null, "category.flow"));
        catalog.Types.Add(new NodeTypeMetadata(
            "node.render_sequence", typeof(RenderSequenceNode), "▦", "var(--node-flow)", null, "category.flow"));
        return catalog;
    }

    private CreatorNodeCatalogTool Tool(CreatorTestDoubles.StubNodeCatalog? catalog = null)
        => new(CreatorTestDoubles.Env(
            services: new CreatorTestDoubles.FakeServices().With<INodeTypeCatalog>(catalog)));

    // ------------------------------------------------------------------
    // 节点目录
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_DescribesNodesWithPropertiesAndPipelines()
    {
        var tool = Tool(CatalogWithBuiltinNodes());

        var result = await tool.ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("\"type\":");
        var payload = JObject.Parse(result.Content!);
        payload["count"]!.Value<int>().Should().Be(4);

        var sequence = payload["nodes"]!.Single(
            n => n["type"]!.Value<string>() == typeof(SequenceNode).FullName);

        // $type 取值使用完整类型名——这是模型写树 JSON 时的关键契约。
        sequence["type_name"]!.Value<string>().Should().Be("SequenceNode");
        sequence["pipelines"]!.Select(t => t.Value<string>()).Should().Equal("pre");
        sequence["label"]!.Value<string>().Should().Be("node.sequence");

        var properties = sequence["properties"]!.ToArray();
        properties.Should().Contain(p => p["name"]!.Value<string>() == "Nodes"
            && p["kind"]!.Value<string>() == "child_list");
        properties.Should().Contain(p => p["name"]!.Value<string>() == "Repeat"
            && p["kind"]!.Value<string>() == "int");
    }

    [Fact]
    public async Task List_FilterByPipeline_OnlyReturnsMatchingNodes()
    {
        var tool = Tool(CatalogWithBuiltinNodes());

        var result = await tool.ExecuteAsync(
            Invocation("""{"action":"list","pipeline":"post"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["count"]!.Value<int>().Should().Be(1);
        payload["nodes"]!.Single()["type_name"]!.Value<string>().Should().Be("PostSequenceNode");
    }

    [Fact]
    public async Task List_EnumProperties_ExposeNumericValues()
    {
        var tool = Tool(CatalogWithBuiltinNodes());

        var result = await tool.ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var fragment = JObject.Parse(result.Content!)["nodes"]!
            .Single(n => n["type_name"]!.Value<string>() == "FragmentNode");

        var role = fragment["properties"]!.Single(p => p["name"]!.Value<string>() == "From");
        role["kind"]!.Value<string>().Should().Be("enum");
        role["enum_values"]!.Should().NotBeEmpty();
        // 树 JSON 里枚举按数字存储，目录必须给出数字值模型才能写出合法 JSON。
        role["enum_values"]!.First()["value"]!.Type.Should().Be(JTokenType.Integer);
    }

    [Fact]
    public async Task List_WithoutCatalogService_IsExplicitFailure()
    {
        var tool = new CreatorNodeCatalogTool(CreatorTestDoubles.Env(services: null));

        var result = await tool.ExecuteAsync(Invocation("""{"action":"list"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.HostServiceUnavailable);
    }

    [Fact]
    public async Task List_InvalidPipelineValue_IsExplicitFailure()
    {
        var tool = Tool(CatalogWithBuiltinNodes());

        var result = await tool.ExecuteAsync(
            Invocation("""{"action":"list","pipeline":"bogus"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    // ------------------------------------------------------------------
    // 工具目录
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListTools_ComesFromTheToolRegistry()
    {
        var registry = Mock.Of<IToolRegistry>(r => r.AllTools == new List<ToolMetadata>
        {
            new("tool.read_file", "tool.read_file.desc", new[] { "category.file_system" }, typeof(string)),
            new("tool.write_file", "tool.write_file.desc", new[] { "category.file_system" }, typeof(int))
        });
        var tool = new CreatorNodeCatalogTool(CreatorTestDoubles.Env(toolRegistry: registry));

        var result = await tool.ExecuteAsync(Invocation("""{"action":"list_tools"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var payload = JObject.Parse(result.Content!);
        payload["count"]!.Value<int>().Should().Be(2);
        // 列表按 type_name 排序，顺序由实现保证（System.Int32 在 System.String 之前）。
        payload["tools"]!.Select(t => t["name_key"]!.Value<string>())
            .Should().Equal("tool.write_file", "tool.read_file");
    }

    // ------------------------------------------------------------------
    // 参数层
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvalidArgumentsJson_IsExplicitFailure()
    {
        var result = await Tool(CatalogWithBuiltinNodes())
            .ExecuteAsync(Invocation("not json"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }

    [Fact]
    public async Task UnknownAction_IsExplicitFailure()
    {
        var result = await Tool(CatalogWithBuiltinNodes())
            .ExecuteAsync(Invocation("""{"action":"explode"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
    }
}
