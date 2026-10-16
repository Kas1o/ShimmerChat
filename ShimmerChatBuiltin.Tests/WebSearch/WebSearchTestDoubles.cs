using System.Collections.ObjectModel;
using SharperLLM.API;
using ShimmerChatBuiltin.WebSearch;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>返回 Key 本身的本地化桩，便于断言错误信息里出现的 Key</summary>
internal sealed class StubLocService : ILocService
{
    public string CurrentCulture => "en-US";
    public IReadOnlyList<string> SupportedCultures => ["en-US"];

    public string this[string key] => key;

    public string Format(string key, params object[] args) =>
        $"{key}[{string.Join(",", args.Select(a => a?.ToString() ?? "null"))}]";

    public string Format(string key, params (string name, object value)[] args) => key;

    public void SetCulture(string culture) { }
}

/// <summary>记录请求并返回预置响应的搜索提供方桩</summary>
internal sealed class FakeWebSearchProvider : IWebSearchProvider
{
    public WebSearchProviderKind Kind => WebSearchProviderKind.Tavily;
    public string DisplayName => "Fake";

    public List<WebSearchQuery> Requests { get; } = [];
    public List<string> FetchedUrls { get; } = [];

    public WebSearchResponse Response { get; set; } = WebSearchResponse.Ok(
    [
        new WebSearchResultItem { Title = "T1", Url = "https://example.com/1", Snippet = "S1" }
    ]);

    public WebFetchResponse FetchResponse { get; set; } =
        WebFetchResponse.Ok("https://example.com/1", "P1", "content");

    public Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
    {
        Requests.Add(query);
        return Task.FromResult(Response);
    }

    public Task<WebFetchResponse> FetchAsync(string url, CancellationToken ct)
    {
        FetchedUrls.Add(url);
        return Task.FromResult(FetchResponse);
    }
}

internal static class WebSearchTestData
{
    /// <summary>构造一份带 API Key 的配置 JSON（模拟设置面板保存后的 KVData 内容）</summary>
    public static string ConfigJson(
        WebSearchProviderKind activeProvider,
        params (WebSearchProviderKind Kind, string ApiKey)[] providerKeys)
    {
        var config = new WebSearchConfig { ActiveProvider = activeProvider };
        foreach (var (kind, apiKey) in providerKeys)
            config.Providers[kind] = new WebSearchProviderSettings { ApiKey = apiKey };

        return Newtonsoft.Json.JsonConvert.SerializeObject(config);
    }

    public static Chat CreateChatWithUserMessage(string content)
    {
        return new Chat
        {
            Name = "TestChat",
            Messages = new ObservableCollection<Message>
            {
                new()
                {
                    timestamp = DateTime.Now,
                    sender = Sender.User,
                    message = new SharperLLM.Util.ChatMessage { Content = content }
                }
            }
        };
    }
}
