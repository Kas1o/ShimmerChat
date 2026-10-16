using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch.Providers
{
    /// <summary>
    /// Exa：Search POST /search，Contents POST /contents，鉴权头 x-api-key。
    /// </summary>
    public sealed class ExaSearchProvider : WebSearchProviderBase
    {
        private const int MinResults = 1;
        private const int MaxResults = 100;

        public ExaSearchProvider(HttpClient http, string apiKey, string? baseUrl, TimeSpan timeout)
            : base(http, WebSearchProviderCatalog.Get(WebSearchProviderKind.Exa), apiKey, baseUrl, timeout)
        {
        }

        public override async Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct)
        {
            var contents = new JObject();
            if (query.IncludeContent)
                contents["text"] = new JObject { ["maxCharacters"] = ContentLimit(query) };
            else
                // 不取正文时也索取 highlights，否则结果里没有可用摘要
                contents["highlights"] = true;

            var payload = new JObject
            {
                ["query"] = query.Query,
                ["numResults"] = Math.Clamp(query.MaxResults, MinResults, MaxResults),
                ["type"] = "auto",
                ["contents"] = contents
            };

            var request = JsonPost("/search", payload, "x-api-key", ApiKey);
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
                    var highlights = item["highlights"] is JArray array
                        ? string.Join(" … ", array.Select(h => h.ToString()).Where(h => !string.IsNullOrWhiteSpace(h)))
                        : null;

                    results.Add(new WebSearchResultItem
                    {
                        Title = item["title"]?.ToString() ?? "",
                        Url = item["url"]?.ToString() ?? "",
                        Snippet = FirstNonEmpty(highlights, item["summary"]?.ToString()),
                        Content = Truncate(item["text"]?.ToString(), query.MaxContentChars),
                        PublishedDate = item["publishedDate"]?.ToString(),
                        Source = item["author"]?.ToString(),
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
                ["text"] = new JObject { ["maxCharacters"] = 20000 }
            };

            var request = JsonPost("/contents", payload, "x-api-key", ApiKey);
            var (body, error) = await SendAsync(request, ct);
            if (body == null)
                return WebFetchResponse.Fail(error!, url: url);

            var (json, parseError) = ParseObject(body);
            if (json == null)
                return WebFetchResponse.Fail(parseError!, body, url);

            if (json["results"] is JArray results && results.Count > 0)
            {
                var first = results[0];
                var content = first["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(content))
                    return WebFetchResponse.Ok(first["url"]?.ToString() ?? url, first["title"]?.ToString(), content);
            }

            // 抓取失败时 results 为空，原因在 statuses 里（每个 URL 一条）
            var statusError = json["statuses"] is JArray statuses
                ? statuses
                    .Select(s => FirstNonEmpty(s["error"]?["tag"]?.ToString(), s["error"]?.ToString(), s["status"]?.ToString()))
                    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                : null;

            return WebFetchResponse.Fail(
                FirstNonEmpty(statusError, "Contents returned no page content.")!,
                body,
                url);
        }

        private static int ContentLimit(WebSearchQuery query) =>
            query.MaxContentChars > 0 ? query.MaxContentChars : 20000;
    }
}
