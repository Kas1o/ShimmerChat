using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch.Providers
{
    /// <summary>
    /// Brave Web Search：GET /res/v1/web/search，鉴权头 X-Subscription-Token。
    /// 只提供搜索，没有单页抓取接口。
    /// </summary>
    public sealed class BraveSearchProvider : WebSearchProviderBase
    {
        private const int MinCount = 1;
        private const int MaxCount = 20;

        public BraveSearchProvider(HttpClient http, string apiKey, string? baseUrl, TimeSpan timeout)
            : base(http, WebSearchProviderCatalog.Get(WebSearchProviderKind.Brave), apiKey, baseUrl, timeout)
        {
        }

        public override async Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
        {
            var parameters = new List<KeyValuePair<string, string?>>
            {
                new("q", query.Query),
                new("count", Math.Clamp(query.MaxResults, MinCount, MaxCount).ToString()),
                // 关掉 <strong> 之类的装饰标记，并额外索取摘要片段
                new("text_decorations", "false"),
                new("extra_snippets", "true"),
                new("safesearch", "moderate"),
                new("search_lang", query.Language),
                new("country", query.Country)
            };

            var request = JsonGet("/res/v1/web/search", parameters, "X-Subscription-Token", ApiKey);
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebSearchResponse.Fail(error!);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebSearchResponse.Fail(parseError!, body);

            var results = new List<WebSearchResultItem>();
            if (json.SelectToken("web.results") is JArray items)
            {
                foreach (var item in items)
                {
                    var snippets = new List<string>();
                    if (item["description"]?.ToString() is { Length: > 0 } description)
                        snippets.Add(description);
                    if (item["extra_snippets"] is JArray extras)
                        snippets.AddRange(extras.Select(e => e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));

                    results.Add(new WebSearchResultItem
                    {
                        Title = item["title"]?.ToString() ?? "",
                        Url = item["url"]?.ToString() ?? "",
                        Snippet = snippets.Count > 0 ? string.Join(" … ", snippets) : null,
                        PublishedDate = item["age"]?.ToString(),
                        Source = item["profile"]?["long_name"]?.ToString()
                    });
                }
            }

            return WebSearchResponse.Ok(results);
        }
    }
}
