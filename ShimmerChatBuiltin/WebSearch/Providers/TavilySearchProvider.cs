using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch.Providers
{
    /// <summary>
    /// Tavily：Search POST /search，Extract POST /extract，Bearer 鉴权。
    /// </summary>
    public sealed class TavilySearchProvider : WebSearchProviderBase
    {
        private const int MinResults = 0;
        private const int MaxResults = 20;

        public TavilySearchProvider(HttpClient http, string apiKey, string? baseUrl, TimeSpan timeout)
            : base(http, WebSearchProviderCatalog.Get(WebSearchProviderKind.Tavily), apiKey, baseUrl, timeout)
        {
        }

        public override async Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["query"] = query.Query,
                ["max_results"] = Math.Clamp(query.MaxResults, MinResults, MaxResults),
                ["search_depth"] = "basic",
                ["topic"] = "general",
                ["include_answer"] = false,
                // markdown 正文按需返回；不请求时服务方不回传 raw_content
                ["include_raw_content"] = query.IncludeContent ? "markdown" : false
            };

            if (!string.IsNullOrWhiteSpace(query.Language))
                payload["language"] = query.Language;

            var request = JsonPost("/search", payload, "Authorization", $"Bearer {ApiKey}");
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebSearchResponse.Fail(error!);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebSearchResponse.Fail(parseError!, body);

            var results = new List<WebSearchResultItem>();
            if (json["results"] is JArray items)
            {
                foreach (var item in items)
                {
                    results.Add(new WebSearchResultItem
                    {
                        Title = item["title"]?.ToString() ?? "",
                        Url = item["url"]?.ToString() ?? "",
                        Snippet = item["content"]?.ToString(),
                        Content = Truncate(item["raw_content"]?.ToString(), query.MaxContentChars),
                        PublishedDate = item["published_date"]?.ToString(),
                        Score = item["score"]?.Value<double?>()
                    });
                }
            }

            return WebSearchResponse.Ok(results);
        }

        public override async Task<WebFetchResponse> FetchAsync(string url, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["urls"] = new JArray(url),
                ["format"] = "markdown"
            };

            var request = JsonPost("/extract", payload, "Authorization", $"Bearer {ApiKey}");
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebFetchResponse.Fail(error!, url: url);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebFetchResponse.Fail(parseError!, body, url);

            if (json["results"] is JArray results && results.Count > 0)
            {
                var first = results[0];
                var content = first["raw_content"]?.ToString();
                if (!string.IsNullOrWhiteSpace(content))
                    return WebFetchResponse.Ok(first["url"]?.ToString() ?? url, first["title"]?.ToString(), content);
            }

            // 全部 URL 失败时 HTTP 仍为 200，失败原因在 failed_results 中
            var failed = json["failed_results"] is JArray failedResults && failedResults.Count > 0
                ? failedResults[0]["error"]?.ToString()
                : null;

            return WebFetchResponse.Fail(
                FirstNonEmpty(failed, "Extract returned no page content.")!,
                body,
                url);
        }
    }
}
