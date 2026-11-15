using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// GitHub Release 检查服务：成功、缓存与每一类失败都必须返回结构化错误码，
/// 而不是把异常抛给 Widget。
/// </summary>
public class GitHubReleaseServiceTests
{
    private static GitHubReleaseService CreateService(
        HttpMessageHandler handler,
        string repository = "Kas1o/ShimmerChat",
        int cacheMinutes = 10)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHubRelease:Repository"] = repository,
                ["GitHubRelease:CacheMinutes"] = cacheMinutes.ToString()
            })
            .Build();

        return new GitHubReleaseService(
            new StubFactory(handler), config, NullLogger<GitHubReleaseService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string ReleaseJson = """
        {
          "tag_name": "v2.0.0",
          "name": "ShimmerChat 2.0",
          "body": "Release notes",
          "published_at": "2026-01-02T03:04:05Z",
          "html_url": "https://github.com/Kas1o/ShimmerChat/releases/tag/v2.0.0",
          "prerelease": false
        }
        """;

    [Fact]
    public async Task GetLatestRelease_Success_ProjectsTheRelease()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeTrue();
        result.Release.Should().NotBeNull();
        result.Release!.Repository.Should().Be("Kas1o/ShimmerChat");
        result.Release.TagName.Should().Be("v2.0.0");
        result.Release.Name.Should().Be("ShimmerChat 2.0");
        result.Release.Body.Should().Be("Release notes");
        result.Release.HtmlUrl.Should().Contain("v2.0.0");
        result.Release.IsPrerelease.Should().BeFalse();
        result.Release.PublishedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetLatestRelease_UsesCacheUntilForceRefresh()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson));
        var service = CreateService(handler);

        await service.GetLatestReleaseAsync(forceRefresh: false);
        await service.GetLatestReleaseAsync(forceRefresh: false);
        handler.Calls.Should().Be(1);

        await service.GetLatestReleaseAsync(forceRefresh: true);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task GetLatestRelease_NotFound_ReturnsNotFoundCode()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.NotFound);
        result.Release.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestRelease_RateLimited_ReturnsRateLimitCode()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.TooManyRequests, "{\"message\":\"rate limited\"}"));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.RateLimited);
    }

    [Fact]
    public async Task GetLatestRelease_ServerError_ReturnsHttpErrorCode()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "boom"));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.HttpError);
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task GetLatestRelease_NetworkFailure_ReturnsNetworkCode()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.Network);
    }

    [Fact]
    public async Task GetLatestRelease_InvalidJson_ReturnsInvalidResponseCode()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{ not json"));
        var service = CreateService(handler);

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.InvalidResponse);
    }

    [Fact]
    public async Task GetLatestRelease_InvalidRepositoryConfig_ReturnsConfigurationCode()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson));
        var service = CreateService(handler, repository: "not-a-repository");

        var result = await service.GetLatestReleaseAsync(forceRefresh: false);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(GitHubReleaseErrorCodes.Configuration);
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetLatestRelease_RequestingCancellation_Throws()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, ReleaseJson));
        var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.GetLatestReleaseAsync(forceRefresh: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetLatestRelease_SendsGitHubHeaders()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return JsonResponse(HttpStatusCode.OK, ReleaseJson);
        });
        var service = CreateService(handler);

        await service.GetLatestReleaseAsync(forceRefresh: false);

        captured.Should().NotBeNull();
        captured!.RequestUri!.ToString().Should().Contain("/repos/Kas1o/ShimmerChat/releases/latest");
        captured.Headers.UserAgent.ToString().Should().Contain("ShimmerChat");
        captured.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
