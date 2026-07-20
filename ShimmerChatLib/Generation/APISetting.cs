using SharperLLM.API;

namespace ShimmerChatLib.Generation
{
	/// <summary>
	/// 管线上层使用的 API 配置抽象。
	/// 只包含管线运行时需要的 Chat 客户端和能力标记。
	/// </summary>
	public class APISetting
	{
		public required IChatCompletionClient ChatClient { get; init; }
		public required bool SupportsStreaming { get; init; }
		public required bool SupportsToolCalling { get; init; }
	}
}
