using SharperLLM.Util;

namespace ShimmerChatLib.Context
{
	public class ContextDocument
	{
		public List<ContextSegment> Segments { get; set; } = new();
		public Dictionary<string, object> SharedState { get; set; } = new();

		public (ChatMessage, PromptBuilder.From)[] GetMessages()
		{
			return Segments
				.Select(s => (s.Message, s.From))
				.ToArray();
		}
	}
}
