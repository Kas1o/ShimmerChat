using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch.Providers
{
    /// <summary>
    /// DeepSeek 联网搜索（Search / Fetch 均为原始 JSON API，Bearer 鉴权）。
    /// Search: POST /v1/tools/search，Fetch: POST /v1/tools/fetch
    /// </summary>
    public sealed class DeepSeekSearchProvider : WebSearchProviderBase
    {
        private const int MinCount = 1;
        private const int MaxCount = 50;

        public DeepSeekSearchProvider(HttpClient http, string apiKey, string? baseUrl, TimeSpan timeout)
            : base(http, WebSearchProviderCatalog.Get(WebSearchProviderKind.DeepSeek), apiKey, baseUrl, timeout)
        {
        }

        public override async Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["query"] = query.Query,
                ["count"] = Math.Clamp(query.MaxResults, MinCount, MaxCount),
                ["need_content"] = query.IncludeContent
            };

            if (!string.IsNullOrWhiteSpace(query.OriginalQuestion))
                payload["ori_question"] = query.OriginalQuestion;

            var request = JsonPost("/v1/tools/search", payload, "Authorization", $"Bearer {ApiKey}");
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
                        // summary（约 600~800 字）比 snippet 更适合喂给模型
                        Snippet = FirstNonEmpty(item["summary"]?.ToString(), item["snippet"]?.ToString()),
                        Content = Truncate(item["content"]?.ToString(), query.MaxContentChars),
                        PublishedDate = item["date"]?.ToString(),
                        Source = item["site_name"]?.ToString(),
                        Score = item["rerank_score"]?.Value<double?>()
                    });
                }
            }

            return WebSearchResponse.Ok(results);
        }

        public override async Task<WebFetchResponse> FetchAsync(string url, CancellationToken ct)
        {
            var request = JsonPost("/v1/tools/fetch", new JObject { ["url"] = url }, "Authorization", $"Bearer {ApiKey}");
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebFetchResponse.Fail(error!, url: url);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebFetchResponse.Fail(parseError!, body, url);

            var content = json["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
                return WebFetchResponse.Fail("Fetch returned no page content.", body, url);

            return WebFetchResponse.Ok(url, json["title"]?.ToString(), content);
        }
    }
}
