using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChatLib.Interface
{
	/// <summary>创建对话的结果。失败时通过错误码与可追踪详情显式返回。</summary>
	public sealed record ChatCreationResult(
		bool Success,
		Guid ChatGuid,
		string? ErrorCode = null,
		string? ErrorMessage = null);

	/// <summary>创建对话的错误码。集中定义，供 UI 映射为本地化提示。</summary>
	public static class ChatCreationErrorCodes
	{
		/// <summary>Agent 不存在或无法反序列化。</summary>
		public const string AgentNotFound = "chat.create.agent_not_found";

		/// <summary>对话或 Agent 的持久化写入失败。</summary>
		public const string StorageFailure = "chat.create.storage_failure";

		/// <summary>调用方提供的 AgentGuid 为空。</summary>
		public const string InvalidAgent = "chat.create.invalid_agent";
	}

	/// <summary>
	/// 创建新对话的统一入口。首页快捷入口与 Agent 页面共用它，
	/// 避免“新对话”的问候语、排序与持久化逻辑出现两份实现。
	/// </summary>
	public interface IChatCreationService
	{
		/// <summary>
		/// 为指定 Agent 创建一个新对话（包含问候语与版本），写入存储并把对话移到列表顶部。
		/// </summary>
		Task<ChatCreationResult> CreateChatAsync(Guid agentGuid, CancellationToken cancellationToken = default);
	}
}
