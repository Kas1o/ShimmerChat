using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChatLib.Interface
{
	/// <summary>GitHub Release 的最小投影，避免把 API 的原始 JSON 带进 UI。</summary>
	public sealed record GitHubReleaseInfo(
		string Repository,
		string TagName,
		string Name,
		string? Body,
		DateTimeOffset? PublishedAt,
		string HtmlUrl,
		bool IsPrerelease);

	/// <summary>检查最新 Release 的结果。失败时 <see cref="Release"/> 为 null。</summary>
	public sealed record GitHubReleaseCheckResult(
		bool Success,
		GitHubReleaseInfo? Release,
		string? ErrorCode,
		string? ErrorMessage,
		DateTime CheckedAtUtc);

	/// <summary>GitHub Release 检查的错误码。集中定义，供 Widget 映射为本地化提示。</summary>
	public static class GitHubReleaseErrorCodes
	{
		/// <summary>仓库没有发布任何 Release，或仓库不存在。</summary>
		public const string NotFound = "github.release.not_found";

		/// <summary>触发 GitHub API 速率限制。</summary>
		public const string RateLimited = "github.release.rate_limited";

		/// <summary>网络异常（DNS、连接失败等）。</summary>
		public const string Network = "github.release.network";

		/// <summary>HTTP 非成功状态码。</summary>
		public const string HttpError = "github.release.http_error";

		/// <summary>请求超时。</summary>
		public const string Timeout = "github.release.timeout";

		/// <summary>响应不是预期的 Release JSON。</summary>
		public const string InvalidResponse = "github.release.invalid_response";

		/// <summary>配置缺失或仓库名非法。</summary>
		public const string Configuration = "github.release.configuration";

		/// <summary>未归类的异常。</summary>
		public const string Unexpected = "github.release.unexpected";
	}

	/// <summary>
	/// 检查 GitHub 上指定仓库的最新 Release。实现负责缓存、User-Agent 与错误归类；
	/// Widget 只消费结构化结果。
	/// </summary>
	public interface IGitHubReleaseService
	{
		/// <summary>当前检查的仓库（"owner/repo"）。</summary>
		string Repository { get; }

		/// <summary>
		/// 获取最新 Release。<paramref name="forceRefresh"/> 为 false 时允许返回短期缓存。
		/// </summary>
		Task<GitHubReleaseCheckResult> GetLatestReleaseAsync(
			bool forceRefresh,
			CancellationToken cancellationToken = default);
	}
}
