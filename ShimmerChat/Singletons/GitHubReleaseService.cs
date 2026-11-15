using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
	/// <summary>
	/// 通过 GitHub REST API 检查仓库最新 Release。结果做短期内存缓存，
	/// 强制刷新时绕过缓存；所有失败都归类为明确错误码，不向 UI 抛异常。
	/// </summary>
	public sealed class GitHubReleaseService : IGitHubReleaseService
	{
		private const string DefaultRepository = "Kas1o/ShimmerChat";
		private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(10);
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<GitHubReleaseService> _logger;
		private readonly SemaphoreSlim _refreshLock = new(1, 1);
		private readonly TimeSpan _cacheDuration;

		private GitHubReleaseCheckResult? _cachedResult;
		private DateTime _cacheExpiresUtc;

		public string Repository { get; }

		public GitHubReleaseService(
			IHttpClientFactory httpClientFactory,
			IConfiguration configuration,
			ILogger<GitHubReleaseService> logger)
		{
			_httpClientFactory = httpClientFactory;
			_logger = logger;

			var configured = configuration["GitHubRelease:Repository"];
			Repository = string.IsNullOrWhiteSpace(configured) ? DefaultRepository : configured.Trim();

			var cacheMinutesRaw = configuration["GitHubRelease:CacheMinutes"];
			_cacheDuration = int.TryParse(cacheMinutesRaw, out var cacheMinutes) && cacheMinutes > 0
				? TimeSpan.FromMinutes(cacheMinutes)
				: DefaultCacheDuration;

			if (!TrySplitRepository(Repository, out _, out _))
			{
				_logger.LogError(
					"[GitHubRelease] Invalid repository value '{Repository}'; expected 'owner/repo'.",
					Repository);
			}
		}

		public async Task<GitHubReleaseCheckResult> GetLatestReleaseAsync(
			bool forceRefresh,
			CancellationToken cancellationToken = default)
		{
			if (!forceRefresh && TryGetFreshCache(out var cached))
				return cached;

			await _refreshLock.WaitAsync(cancellationToken);
			try
			{
				if (!forceRefresh && TryGetFreshCache(out cached))
					return cached;

				var result = await FetchAsync(cancellationToken);
				if (result.Success)
				{
					_cachedResult = result;
					_cacheExpiresUtc = DateTime.UtcNow.Add(_cacheDuration);
				}
				return result;
			}
			finally
			{
				_refreshLock.Release();
			}
		}

		private bool TryGetFreshCache(out GitHubReleaseCheckResult result)
		{
			if (_cachedResult is { Success: true } cached && DateTime.UtcNow < _cacheExpiresUtc)
			{
				result = cached;
				return true;
			}

			result = null!;
			return false;
		}

		private async Task<GitHubReleaseCheckResult> FetchAsync(CancellationToken cancellationToken)
		{
			var checkedAt = DateTime.UtcNow;

			if (!TrySplitRepository(Repository, out var owner, out var repo))
			{
				return Failure(
					GitHubReleaseErrorCodes.Configuration,
					$"Invalid repository value '{Repository}'; expected 'owner/repo'.",
					checkedAt);
			}

			var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";

			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(RequestTimeout);

			try
			{
				var client = _httpClientFactory.CreateClient();
				using var request = new HttpRequestMessage(HttpMethod.Get, url);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
				request.Headers.UserAgent.ParseAdd($"ShimmerChat/{GetCurrentVersion()}");
				request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

				using var response = await client.SendAsync(
					request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					_logger.LogInformation(
						"[GitHubRelease] Repository {Repository} has no published release (404).",
						Repository);
					return Failure(
						GitHubReleaseErrorCodes.NotFound,
						"GitHub returned 404: the repository does not exist or has no release.",
						checkedAt);
				}

				if (!response.IsSuccessStatusCode)
				{
					var body = await ReadBodySnippetAsync(response, timeoutCts.Token);
					var code = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
						? GitHubReleaseErrorCodes.RateLimited
						: GitHubReleaseErrorCodes.HttpError;
					var message = $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}. {body}".Trim();
					_logger.LogError(
						"[GitHubRelease] Request for {Repository} failed: {Message}",
						Repository, message);
					return Failure(code, message, checkedAt);
				}

				await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
				var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
					stream,
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
					timeoutCts.Token);

				if (dto == null || string.IsNullOrWhiteSpace(dto.TagName))
				{
					_logger.LogError(
						"[GitHubRelease] GitHub returned an unexpected release payload for {Repository}.",
						Repository);
					return Failure(
						GitHubReleaseErrorCodes.InvalidResponse,
						"GitHub returned a release payload without a tag name.",
						checkedAt);
				}

				var release = new GitHubReleaseInfo(
					Repository,
					dto.TagName,
					string.IsNullOrWhiteSpace(dto.Name) ? dto.TagName : dto.Name,
					dto.Body,
					dto.PublishedAt,
					dto.HtmlUrl ?? string.Empty,
					dto.Prerelease);

				return new GitHubReleaseCheckResult(true, release, null, null, checkedAt);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 调用方主动取消（页面销毁）：不要伪装成一次失败检查。
				throw;
			}
			catch (OperationCanceledException ex)
			{
				_logger.LogError(ex, "[GitHubRelease] Request for {Repository} timed out.", Repository);
				return Failure(GitHubReleaseErrorCodes.Timeout, "The GitHub request timed out.", checkedAt);
			}
			catch (HttpRequestException ex)
			{
				_logger.LogError(ex, "[GitHubRelease] Network error while checking {Repository}.", Repository);
				return Failure(GitHubReleaseErrorCodes.Network, ex.Message, checkedAt);
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "[GitHubRelease] Failed to parse the release payload for {Repository}.", Repository);
				return Failure(GitHubReleaseErrorCodes.InvalidResponse, ex.Message, checkedAt);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[GitHubRelease] Unexpected error while checking {Repository}.", Repository);
				return Failure(GitHubReleaseErrorCodes.Unexpected, ex.Message, checkedAt);
			}
		}

		private GitHubReleaseCheckResult Failure(string code, string message, DateTime checkedAt)
			=> new(false, null, code, message, checkedAt);

		private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			body = body.ReplaceLineEndings(" ").Trim();
			return body.Length > 200 ? body[..200] : body;
		}

		private static bool TrySplitRepository(string value, out string owner, out string repo)
		{
			owner = string.Empty;
			repo = string.Empty;
			if (string.IsNullOrWhiteSpace(value))
				return false;

			var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
				return false;

			owner = parts[0];
			repo = parts[1];
			return true;
		}

		private static string GetCurrentVersion()
			=> Assembly.GetEntryAssembly()
				?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion
				?? "unknown";

		private sealed class GitHubReleaseDto
		{
			[JsonPropertyName("tag_name")]
			public string? TagName { get; set; }

			[JsonPropertyName("name")]
			public string? Name { get; set; }

			[JsonPropertyName("body")]
			public string? Body { get; set; }

			[JsonPropertyName("published_at")]
			public DateTimeOffset? PublishedAt { get; set; }

			[JsonPropertyName("html_url")]
			public string? HtmlUrl { get; set; }

			[JsonPropertyName("prerelease")]
			public bool Prerelease { get; set; }
		}
	}
}
