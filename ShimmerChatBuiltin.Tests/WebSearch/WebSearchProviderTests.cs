using System.Net;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.WebSearch;
using ShimmerChatBuiltin.WebSearch.Providers;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>
/// 五个联网搜索服务的协议适配测试：请求组装（地址 / 鉴权 / 参数）与响应解析。
/// 网络层全部用桩替代，不发出真实请求。
/// </summary>
public class WebSearchProviderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static WebSearchQuery Query(int maxResults = 3, bool includeContent = false) => new()
    {
        Query = "shimmer chat",
        MaxResults = maxResults,
        IncludeContent = includeContent,
        MaxContentChars = 100,
        Language = "zh",
        Country = "CN"
    };

    [Fact]
    public async Task DeepSeek_Search_MapsSummaryContentAndMetadata()
    {
        const string body = """
        {
          "results": [
            {
              "rerank_score": 2.4,
              "title": "T1",
              "url": "https://example.com/1",
              "snippet": "short",
              "summary": "long summary",
              "content": "page body",
              "site_name": "Example",
              "date": "2026-06-01T00:00:00+00:00"
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(body);
        var provider = new DeepSeekSearchProvider(handler.CreateClient(), "ds-key", null, Timeout);

        var response = await provider.SearchAsync(Query(includeContent: true), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Results.Should().HaveCount(1);
        response.Results[0].Title.Should().Be("T1");
        response.Results[0].Url.Should().Be("https://example.com/1");
        // 长摘要（summary）优先作为 Snippet 喂给模型
        response.Results[0].Snippet.Should().Be("long summary");
        response.Results[0].Content.Should().Be("page body");
        response.Results[0].Source.Should().Be("Example");
        response.Results[0].Score.Should().Be(2.4);

        handler.LastRequestUri!.AbsolutePath.Should().Be("/v1/tools/search");
        handler.LastAuthorizationHeader.Should().Be("Bearer ds-key");
        var payload = JObject.Parse(handler.LastRequestBody!);
        payload["query"]!.ToString().Should().Be("shimmer chat");
        payload["count"]!.Value<int>().Should().Be(3);
        payload["need_content"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task DeepSeek_Fetch_ReturnsMarkdownContent()
    {
        var handler = new StubHttpMessageHandler("""{"title":"Page","content":"# Hello"}""");
        var provider = new DeepSeekSearchProvider(handler.CreateClient(), "ds-key", null, Timeout);

        var response = await provider.FetchAsync("https://example.com/a", CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Title.Should().Be("Page");
        response.Content.Should().Be("# Hello");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/v1/tools/fetch");
        JObject.Parse(handler.LastRequestBody!)["url"]!.ToString().Should().Be("https://example.com/a");
    }

    [Fact]
    public async Task Tavily_Search_ParsesResultsAndHonoursBaseUrlOverride()
    {
        const string body = """
        {
          "results": [
            {"title":"T","url":"https://a.test","content":"snippet","raw_content":"full","score":0.9,"published_date":"2026-01-02"}
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(body);
        var provider = new TavilySearchProvider(handler.CreateClient(), "tvly-key", "https://proxy.test/", Timeout);

        var response = await provider.SearchAsync(Query(includeContent: true), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Results[0].Snippet.Should().Be("snippet");
        response.Results[0].Content.Should().Be("full");
        response.Results[0].Score.Should().Be(0.9);
        response.Results[0].PublishedDate.Should().Be("2026-01-02");

        handler.LastRequestUri!.ToString().Should().Be("https://proxy.test/search");
        handler.LastAuthorizationHeader.Should().Be("Bearer tvly-key");
        var payload = JObject.Parse(handler.LastRequestBody!);
        payload["include_raw_content"]!.ToString().Should().Be("markdown");
        payload["language"]!.ToString().Should().Be("zh");
    }

    [Fact]
    public async Task Tavily_Fetch_ReportsFailedUrls()
    {
        var handler = new StubHttpMessageHandler(
            """{"results":[],"failed_results":[{"url":"https://a.test","error":"blocked by site"}]}""");
        var provider = new TavilySearchProvider(handler.CreateClient(), "tvly-key", null, Timeout);

        var response = await provider.FetchAsync("https://a.test", CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Error.Should().Be("blocked by site");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/extract");
    }

    [Fact]
    public async Task Brave_Search_JoinsSnippetsAndSendsSubscriptionToken()
    {
        const string body = """
        {
          "web": {
            "results": [
              {"title":"B","url":"https://b.test","description":"main","age":"2026-01-01","extra_snippets":["extra1","extra2"]}
            ]
          }
        }
        """;

        var handler = new StubHttpMessageHandler(body);
        var provider = new BraveSearchProvider(handler.CreateClient(), "brave-key", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Results[0].Snippet.Should().Be("main … extra1 … extra2");
        response.Results[0].PublishedDate.Should().Be("2026-01-01");

        handler.LastSubscriptionTokenHeader.Should().Be("brave-key");
        handler.LastAuthorizationHeader.Should().BeNull();
        var query = handler.LastRequestUri!.Query;
        query.Should().Contain("q=shimmer%20chat");
        query.Should().Contain("count=3");
        query.Should().Contain("search_lang=zh");
        query.Should().Contain("country=CN");
    }

    [Fact]
    public async Task Brave_Fetch_IsReportedAsUnsupported()
    {
        var handler = new StubHttpMessageHandler("{}");
        var provider = new BraveSearchProvider(handler.CreateClient(), "brave-key", null, Timeout);

        var response = await provider.FetchAsync("https://b.test", CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("does not provide");
        handler.LastRequestUri.Should().BeNull();
    }

    [Fact]
    public async Task Exa_Search_UsesHighlightsWhenContentNotRequested()
    {
        const string body = """
        {"results":[{"title":"E","url":"https://e.test","publishedDate":"2026-03-01","author":"A","highlights":["h1","h2"]}]}
        """;

        var handler = new StubHttpMessageHandler(body);
        var provider = new ExaSearchProvider(handler.CreateClient(), "exa-key", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Results[0].Snippet.Should().Be("h1 … h2");
        response.Results[0].Source.Should().Be("A");

        handler.LastApiKeyHeader.Should().Be("exa-key");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/search");
        var payload = JObject.Parse(handler.LastRequestBody!);
        payload["numResults"]!.Value<int>().Should().Be(3);
        payload.SelectToken("contents.highlights")!.Value<bool>().Should().BeTrue();
        payload.SelectToken("contents.text").Should().BeNull();
    }

    [Fact]
    public async Task Exa_Fetch_ReadsTextFromContents()
    {
        var handler = new StubHttpMessageHandler(
            """{"results":[{"url":"https://e.test/1","title":"E1","text":"body text"}]}""");
        var provider = new ExaSearchProvider(handler.CreateClient(), "exa-key", null, Timeout);

        var response = await provider.FetchAsync("https://e.test/1", CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Title.Should().Be("E1");
        response.Content.Should().Be("body text");
        handler.LastRequestUri!.AbsolutePath.Should().Be("/contents");
    }

    [Fact]
    public async Task SerpApi_Search_MapsOrganicResultsAndSendsApiKeyInQuery()
    {
        const string body = """
        {"organic_results":[{"position":1,"title":"S","link":"https://s.test","snippet":"snip","date":"2026-02-02","source":"Src"}]}
        """;

        var handler = new StubHttpMessageHandler(body);
        var provider = new SerpApiSearchProvider(handler.CreateClient(), "serp-key", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Results[0].Url.Should().Be("https://s.test");
        response.Results[0].Snippet.Should().Be("snip");
        response.Results[0].Source.Should().Be("Src");

        handler.LastRequestUri!.AbsolutePath.Should().Be("/search.json");
        handler.LastRequestUri.Query.Should().Contain("api_key=serp-key");
        handler.LastRequestUri.Query.Should().Contain("engine=google");
        handler.LastRequestUri.Query.Should().Contain("hl=zh");
        handler.LastRequestUri.Query.Should().Contain("gl=CN");
    }

    [Fact]
    public async Task SerpApi_Search_ReportsErrorFieldFromSuccessfulHttpResponse()
    {
        var handler = new StubHttpMessageHandler("""{"error":"Invalid API key"}""");
        var provider = new SerpApiSearchProvider(handler.CreateClient(), "bad", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Error.Should().Be("Invalid API key");
    }

    [Fact]
    public async Task HttpError_IsSurfacedWithProviderMessage()
    {
        var handler = new StubHttpMessageHandler(
            """{"error":{"message":"Invalid API key","type":"invalid_request_error"}}""",
            HttpStatusCode.Unauthorized);
        var provider = new TavilySearchProvider(handler.CreateClient(), "bad", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("HTTP 401");
        response.Error.Should().Contain("Invalid API key");
    }

    [Fact]
    public async Task InvalidJson_IsReportedAsParseError()
    {
        var handler = new StubHttpMessageHandler("<html>gateway error</html>");
        var provider = new TavilySearchProvider(handler.CreateClient(), "key", null, Timeout);

        var response = await provider.SearchAsync(Query(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Failed to parse response JSON");
    }

    [Fact]
    public async Task Content_IsTruncatedToConfiguredLimit()
    {
        var longText = new string('x', 500);
        var handler = new StubHttpMessageHandler(
            $$"""{"results":[{"title":"T","url":"https://a.test","content":"s","raw_content":"{{longText}}"}]}""");
        var provider = new TavilySearchProvider(handler.CreateClient(), "key", null, Timeout);

        var response = await provider.SearchAsync(Query(includeContent: true), CancellationToken.None);

        response.Results[0].Content!.Length.Should().Be(101); // 100 字符 + 省略号
        response.Results[0].Content.Should().EndWith("…");
    }
}
