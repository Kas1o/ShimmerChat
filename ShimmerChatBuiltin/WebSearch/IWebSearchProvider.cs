using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 联网搜索服务方契约。实现类只负责协议细节：
    /// 组装请求 → 解析响应 → 返回 <see cref="WebSearchResponse"/> / <see cref="WebFetchResponse"/>，
    /// 不吞异常、不做静默降级：失败必须写进返回值的 Error / ErrorDetails。
    /// </summary>
    public interface IWebSearchProvider
    {
        WebSearchProviderKind Kind { get; }

        /// <summary>该服务方的英文技术名，用于给模型看的文本</summary>
        string DisplayName { get; }

        /// <summary>搜索接口返回结果</summary>
        Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct);

        /// <summary>抓取单个网页正文。不支持的提供方返回明确失败，由调用方决定是否添加该能力。</summary>
        Task<WebFetchResponse> FetchAsync(string url, CancellationToken ct);
    }

    /// <summary>
    /// 提供方实现基类：统一超时、鉴权头、JSON 解析与错误信息提取。
    /// </summary>
    public abstract class WebSearchProviderBase : IWebSearchProvider
    {
        private readonly WebSearchProviderDescriptor _descriptor;

        protected WebSearchProviderBase(
            HttpClient http,
            WebSearchProviderDescriptor descriptor,
            string apiKey,
            string? baseUrl,
            TimeSpan timeout)
        {
            Http = http;
            _descriptor = descriptor;
            ApiKey = apiKey;
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? descriptor.DefaultBaseUrl
                : baseUrl.Trim().TrimEnd('/');
            Timeout = timeout;
        }

        public WebSearchProviderKind Kind => _descriptor.Kind;

        /// <summary>该服务方的英文技术名，用于给模型看的文本</summary>
        public string DisplayName => _descriptor.DisplayName;

        protected HttpClient Http { get; }
        protected string ApiKey { get; }
        protected string BaseUrl { get; }
        protected TimeSpan Timeout { get; }

        public abstract Task<WebSearchResponse> SearchAsync(WebSearchQuery query, CancellationToken ct);

        public virtual Task<WebFetchResponse> FetchAsync(string url, CancellationToken ct) =>
            Task.FromResult(WebFetchResponse.Fail(
                $"{DisplayName} does not provide a web page fetch API.", url: url));

        // ── 请求构造 ──────────────────────────────────────────────

        protected HttpRequestMessage JsonPost(string path, JObject body, string headerName, string headerValue)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(headerValue))
                request.Headers.TryAddWithoutValidation(headerName, headerValue);
            return request;
        }

        protected HttpRequestMessage JsonGet(
            string path, IEnumerable<KeyValuePair<string, string?>> query, string headerName, string headerValue)
        {
            var queryString = string.Join("&", query
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

            var url = string.IsNullOrEmpty(queryString) ? $"{BaseUrl}{path}" : $"{BaseUrl}{path}?{queryString}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrEmpty(headerValue))
                request.Headers.TryAddWithoutValidation(headerName, headerValue);
            return request;
        }

        // ── 发送与错误处理 ────────────────────────────────────────

        /// <summary>
        /// 发送请求并读取响应体。成功时返回 Body；失败时返回 Error（含服务方错误信息）。
        /// 调用方的 CancellationToken 被取消时原样抛出，不被当成超时吞掉。
        /// </summary>
        protected async Task<(string? Body, string? Error)> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            try
            {
                using var response = await Http.SendAsync(request, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                    return (null, $"{DisplayName} returned HTTP {(int)response.StatusCode} ({response.StatusCode}): {ExtractError(body)}");

                return (body, null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null, $"{DisplayName} request timed out after {Timeout.TotalSeconds:0.#}s.");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"{DisplayName} request failed: {ex.Message}");
            }
        }

        /// <summary>解析响应 JSON；形状不符或语法错误都返回明确错误。</summary>
        protected static (JObject? Json, string? Error) ParseObject(string body)
        {
            try
            {
                var token = JToken.Parse(body);
                if (token is JObject obj)
                    return (obj, null);
                return (null, $"Unexpected response shape: expected a JSON object, got {token.Type}.");
            }
            catch (JsonException ex)
            {
                return (null, $"Failed to parse response JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// 从错误响应体中提取可读信息。不同服务方形状不同，逐一尝试常见字段，最后回退到截断的原文。
        /// </summary>
        protected static string ExtractError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "(empty response body)";

            try
            {
                var token = JToken.Parse(body);
                var message = token is JObject obj
                    ? FirstNonEmpty(
                        obj.SelectToken("error.message")?.ToString(),
                        obj["error"] is JValue ? obj["error"]!.ToString() : null,
                        obj["message"]?.ToString(),
                        obj["detail"]?.ToString())
                    : null;

                if (!string.IsNullOrWhiteSpace(message))
                    return message!;
            }
            catch (JsonException)
            {
                // 非 JSON 错误体，直接截断返回
            }

            const int maxLength = 500;
            return body.Length > maxLength ? body[..maxLength] + "…" : body;
        }

        protected static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        /// <summary>按上限截断文本，超出时追加省略标记</summary>
        protected static string? Truncate(string? text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text[..maxChars] + "…";
        }
    }

    /// <summary>
    /// 共享 HttpClient。实例复用连接池；超时由每次请求自己的 CTS 控制，
    /// 因此不设置 HttpClient.Timeout。
    /// </summary>
    public static class WebSearchHttp
    {
        public static HttpClient Client { get; } = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}
