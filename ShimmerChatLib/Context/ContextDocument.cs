using SharperLLM.Util;

namespace ShimmerChatLib.Context
{
	public class ContextDocument
	{
		public required PromptBuilder Template { get; init; }
		public List<ContextSegment> Segments { get; set; } = new();
		public Dictionary<string, object> SharedState { get; set; } = new();

		public void RenderTo(PromptBuilder target)
		{
			target.Messages = Segments
				.Select(s => (s.Message, s.From))
				.ToArray();
		}
	}
}
