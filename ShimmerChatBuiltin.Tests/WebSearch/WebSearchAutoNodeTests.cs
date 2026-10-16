using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatBuiltin.Tests.Generation.Nodes;
using ShimmerChatBuiltin.WebSearch;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>
/// 自动联网搜索节点：查询词来源、注入位置、失败策略与改写分支。
/// 搜索调用全部用提供方桩替换，不发起网络请求。
/// </summary>
public class WebSearchAutoNodeTests : NodeTestBase
{
    private readonly FakeWebSearchProvider _provider = new();

    private PreNodeExecutionContext CreateContextWith(Chat chat, APISetting? api = null)
    {
        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = new StubLocService(),
            DebugOutput = DebugOutputMock.Object,
            ImageAttachments = ImageAttachmentsMock.Object,
            Chat = chat,
            Agent = Agent.Create("TestAgent", "")
        };

        var env = new PreGenerationEnv(persistent);
        env.Transient.API = api;
        return new PreNodeExecutionContext(env);
    }

    private WebSearchAutoNode CreateNode(WebSearchProviderResolution? resolution = null)
    {
        var node = new WebSearchAutoNode { Name = "AutoSearch" };
        node.ProviderResolver = (_, _) => resolution
            ?? new WebSearchProviderResolution { Provider = _provider, ResolvedKind = WebSearchProviderKind.Tavily };
        return node;
    }

    private void SetupConfig(string? json) =>
        KvMock.Setup(k => k.Read("WebSearch", "Config")).Returns(json);

    [Fact]
    public async Task SuccessfulSearch_AppendsFragmentAtEndWithMetadata()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var chat = WebSearchTestData.CreateChatWithUserMessage("latest news about rust");
        var context = CreateContextWith(chat);
        context.Env.Transient.Fragments.Add(new ContextSegment
        {
            Message = new ChatMessage { Content = "existing" },
            From = PromptBuilder.From.system
        });

        var result = await CreateNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Fragments.Should().HaveCount(2);

        var injected = context.Env.Transient.Fragments[1];
        injected.From.Should().Be(PromptBuilder.From.system);
        injected.SourceType.Should().Be(typeof(WebSearchAutoNode));
        injected.Message.Content.Should().Contain("latest news about rust");
        injected.Message.Content.Should().Contain("https://example.com/1");
        injected.Metadata["query"].Should().Be("latest news about rust");
        injected.Metadata["provider"].Should().Be("Fake");
        injected.Metadata["resultCount"].Should().Be(1);

        _provider.Requests.Should().HaveCount(1);
        _provider.Requests[0].Query.Should().Be("latest news about rust");
        // 未指定条数时使用全局配置
        _provider.Requests[0].MaxResults.Should().Be(5);
    }

    [Fact]
    public async Task MaxResultsAndHeaderAreApplied()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var node = CreateNode();
        node.MaxResults = 2;
        node.Header = "HEADER-TEXT";
        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q"));

        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        _provider.Requests[0].MaxResults.Should().Be(2);
        context.Env.Transient.Fragments[0].Message.Content.Should().StartWith("HEADER-TEXT");
    }

    [Fact]
    public async Task StaticQuerySource_UsesConfiguredText()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var node = CreateNode();
        node.QuerySource = WebSearchQuerySource.StaticText;
        node.StaticQuery = "fixed query";
        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("ignored input"));

        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        _provider.Requests[0].Query.Should().Be("fixed query");
    }

    [Fact]
    public async Task StaticQuerySource_WithEmptyText_Fails()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var node = CreateNode();
        node.QuerySource = WebSearchQuerySource.StaticText;
        node.StaticQuery = "   ";

        var result = await node.ExecuteAsync(CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q")));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        result.Message.Should().Contain("node_err.web_search_static_query_empty");
        _provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoUserMessage_Fails()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var chat = new Chat { Name = "empty" };

        var result = await CreateNode().ExecuteAsync(CreateContextWith(chat));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        result.Message.Should().Contain("node_err.web_search_no_user_input");
    }

    [Fact]
    public async Task MissingApiKey_FailsWithoutCallingProvider()
    {
        // 真实工厂解析：配置里没有 Key 时必须明确失败，而不是静默跳过搜索
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily));
        var node = new WebSearchAutoNode { Name = "AutoSearch" };
        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q"));

        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.web_search_api_key_missing");
        result.Message.Should().Contain("Tavily");
        result.NodeId.Should().Be(node.Id);
    }

    [Fact]
    public async Task SearchFailure_IsReportedWithProviderError()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        _provider.Response = WebSearchResponse.Fail("rate limited", "raw body");

        var result = await CreateNode().ExecuteAsync(CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q")));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ServiceError);
        result.Message.Should().Contain("rate limited");
        result.Details.Should().Be("raw body");
    }

    [Fact]
    public async Task SkipAndLog_SwallowsFailureButLogsIt()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        _provider.Response = WebSearchResponse.Fail("rate limited");
        var node = CreateNode();
        node.OnFailure = WebSearchFailureBehavior.SkipAndLog;

        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q"));
        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Fragments.Should().BeEmpty();
        DebugOutputMock.Verify(d => d.Write("WebSearchAutoNode", "WebSearch",
            It.Is<string>(m => m.Contains("rate limited"))), Times.Once);
    }

    [Fact]
    public async Task EmptyResults_SkipInjectionAndLog()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        _provider.Response = WebSearchResponse.Ok([]);
        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q"));

        var result = await CreateNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Fragments.Should().BeEmpty();
        DebugOutputMock.Verify(d => d.Write("WebSearchAutoNode", "WebSearch",
            It.Is<string>(m => m.Contains("no results"))), Times.Once);
    }

    [Fact]
    public async Task LongInput_IsTruncatedToMaxQueryChars()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var input = string.Join(' ', Enumerable.Repeat("word", 100));
        var node = CreateNode();
        node.MaxQueryChars = 50;

        var result = await node.ExecuteAsync(CreateContextWith(WebSearchTestData.CreateChatWithUserMessage(input)));

        result.Success.Should().BeTrue();
        _provider.Requests[0].Query.Length.Should().BeLessThanOrEqualTo(50);
        _provider.Requests[0].Query.Should().NotEndWith(" ");
    }

    [Fact]
    public async Task RewriteQuery_UsesApiAndStripsQuotes()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var chatClient = new Mock<IChatCompletionClient>();
        chatClient
            .Setup(c => c.GenerateAsync(It.IsAny<PromptBuilder>()))
            .ReturnsAsync(new ResponseEx
            {
                Body = new ChatMessage { Content = "\"rust async book\"." },
                FinishReason = FinishReason.Stop
            });

        var api = new APISetting
        {
            ChatClient = chatClient.Object,
            SupportsStreaming = true,
            SupportsToolCalling = true
        };

        var node = CreateNode();
        node.RewriteQueryWithLlm = true;

        var context = CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("那个异步的书叫啥来着"), api);
        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        _provider.Requests[0].Query.Should().Be("rust async book");
    }

    [Fact]
    public async Task RewriteQuery_WithoutApi_FailsWithApiUnavailable()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var node = CreateNode();
        node.RewriteQueryWithLlm = true;

        var result = await node.ExecuteAsync(
            CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q"), api: null));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ApiUnavailable);
        result.Message.Should().Contain("node_err.web_search_no_api");
    }

    [Fact]
    public async Task RewritePrompt_WithoutPlaceholder_Fails()
    {
        SetupConfig(WebSearchTestData.ConfigJson(WebSearchProviderKind.Tavily, (WebSearchProviderKind.Tavily, "key")));
        var node = CreateNode();
        node.RewriteQueryWithLlm = true;
        node.RewritePrompt = "no placeholder here";

        var result = await node.ExecuteAsync(CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q")));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        result.Message.Should().Contain("node_err.web_search_rewrite_placeholder_missing");
    }

    [Fact]
    public async Task InvalidConfig_Fails()
    {
        SetupConfig("{ not json");

        var result = await CreateNode().ExecuteAsync(
            CreateContextWith(WebSearchTestData.CreateChatWithUserMessage("q")));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        result.Message.Should().Contain("node_err.web_search_config_invalid");
        result.Details.Should().Contain("not valid JSON");
    }
}
