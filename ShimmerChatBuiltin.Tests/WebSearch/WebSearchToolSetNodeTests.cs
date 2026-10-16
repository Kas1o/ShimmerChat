using ShimmerChatBuiltin.Tests.Generation.Nodes;
using ShimmerChatBuiltin.WebSearch;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>
/// 联网搜索工具集节点：按提供方能力决定是否添加抓取工具，以及缺 Key / 未知服务时的明确失败。
/// </summary>
public class WebSearchToolSetNodeTests : NodeTestBase
{
    private PreNodeExecutionContext CreateContext()
    {
        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = new StubLocService(),
            DebugOutput = DebugOutputMock.Object,
            ImageAttachments = ImageAttachmentsMock.Object,
            Chat = new Chat { Name = "TestChat" },
            Agent = Agent.Create("TestAgent", "")
        };

        return new PreNodeExecutionContext(new PreGenerationEnv(persistent));
    }

    private void SetupConfig(string? json) =>
        KvMock.Setup(k => k.Read("WebSearch", "Config")).Returns(json);

    [Fact]
    public async Task ProviderWithFetch_AddsSearchAndFetchTools()
    {
        SetupConfig(WebSearchTestData.ConfigJson(
            WebSearchProviderKind.DeepSeek, (WebSearchProviderKind.DeepSeek, "ds-key")));
        var context = CreateContext();

        var result = await new WebSearchToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().BeEquivalentTo(["web_search", "web_fetch"]);
    }

    [Fact]
    public async Task ProviderWithoutFetch_AddsSearchToolOnlyAndLogs()
    {
        SetupConfig(WebSearchTestData.ConfigJson(
            WebSearchProviderKind.Brave, (WebSearchProviderKind.Brave, "brave-key")));
        var context = CreateContext();

        var result = await new WebSearchToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().BeEquivalentTo(["web_search"]);
        DebugOutputMock.Verify(d => d.Write("WebSearchToolSetNode", "WebSearch",
            It.Is<string>(m => m.Contains("no fetch API"))), Times.Once);
    }

    [Fact]
    public async Task NodeProviderOverride_WinsOverConfiguredDefault()
    {
        SetupConfig(WebSearchTestData.ConfigJson(
            WebSearchProviderKind.Brave,
            (WebSearchProviderKind.Brave, "brave-key"),
            (WebSearchProviderKind.Tavily, "tvly-key")));
        var node = new WebSearchToolSetNode { Provider = WebSearchProviderKind.Tavily };
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        // Tavily 支持抓取，所以即使默认服务是 Brave，也应加上抓取工具
        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().BeEquivalentTo(["web_search", "web_fetch"]);
    }

    [Fact]
    public async Task DisabledTools_AddNothing()
    {
        SetupConfig(WebSearchTestData.ConfigJson(
            WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "tvly-key")));
        var node = new WebSearchToolSetNode { EnableSearchTool = false, EnableFetchTool = false };
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingApiKey_Fails()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily));
        var node = new WebSearchToolSetNode();

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.web_search_api_key_missing");
        result.NodeId.Should().Be(node.Id);
    }

    [Fact]
    public async Task InvalidConfig_Fails()
    {
        SetupConfig("{ broken");

        var result = await new WebSearchToolSetNode().ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        result.Details.Should().Contain("not valid JSON");
    }
}
