using System.Net;
using System.Net.Http;

namespace ShimmerChatBuiltin.Tests.WebSearch;

/// <summary>
/// 记录请求内容并返回预置响应的 HttpMessageHandler，用于测试提供方的请求组装与响应解析，
/// 测试中不发起真实网络请求。
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _statusCode = statusCode;
    }

    public Uri? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastAuthorizationHeader { get; private set; }
    public string? LastSubscriptionTokenHeader { get; private set; }
    public string? LastApiKeyHeader { get; private set; }

    public HttpClient CreateClient() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.Headers.TryGetValues("Authorization", out var authorization))
            LastAuthorizationHeader = authorization.FirstOrDefault();
        if (request.Headers.TryGetValues("X-Subscription-Token", out var token))
            LastSubscriptionTokenHeader = token.FirstOrDefault();
        if (request.Headers.TryGetValues("x-api-key", out var apiKey))
            LastApiKeyHeader = apiKey.FirstOrDefault();

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
