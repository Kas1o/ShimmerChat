using SharperLLM.Util;

namespace ShimmerChatLib.Context
{
	public class ContextSegment
	{
		public required ChatMessage Message { get; set; }
		public required PromptBuilder.From From { get; set; }
		public Type? SourceType { get; init; }
		public Dictionary<string, object> Metadata { get; init; } = new();
	}
}
