using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch.Providers
{
    /// <summary>
    /// SerpApi（Google 结果）：GET /search.json，api_key 走查询参数。
    /// 只提供搜索，没有单页抓取接口。
    /// </summary>
    public sealed class SerpApiSearchProvider : WebSearchProviderBase
    {
        private const int MinNum = 1;
        private const int MaxNum = 100;

        public SerpApiSearchProvider(HttpClient http, string apiKey, string? baseUrl, TimeSpan timeout)
            : base(http, WebSearchProviderCatalog.Get(WebSearchProviderKind.SerpApi), apiKey, baseUrl, timeout)
        {
        }

        public override async Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
        {
            var parameters = new List<KeyValuePair<string, string?>>
            {
                new("engine", "google"),
                new("q", query.Query),
                new("api_key", ApiKey),
                new("num", Math.Clamp(query.MaxResults, MinNum, MaxNum).ToString()),
                new("hl", query.Language),
                new("gl", query.Country)
            };

            var request = JsonGet("/search.json", parameters, headerName: "", headerValue: "");
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebSearchResponse.Fail(error!);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebSearchResponse.Fail(parseError!, body);

            // 失败时 SerpApi 会在 200 响应里带 error 字段
            if (json["error"]?.ToString() is { Length: > 0 } apiError)
                return WebSearchResponse.Fail(apiError, body);

            var results = new List<WebSearchResultItem>();
            if (json["organic_results"] is JArray items)
            {
                foreach (var item in items)
                {
                    results.Add(new WebSearchResultItem
                    {
                        Title = item["title"]?.ToString() ?? "",
                        Url = item["link"]?.ToString() ?? "",
                        Snippet = item["snippet"]?.ToString(),
                        PublishedDate = item["date"]?.ToString(),
                        Source = item["source"]?.ToString()
                    });
                }
            }

            return WebSearchResponse.Ok(results);
        }
    }
}
