using SharperLLM.Util;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public static class Utils
	{
		public static PromptBuilder ToPromptBuilder(this Chat chat, string system)
		{
			var pb = new PromptBuilder();
			var p = chat.Messages.Select(
				x =>
				(x.message, x.sender.ToLower() switch
				{
					"user" => PromptBuilder.From.user,
					"system" => PromptBuilder.From.system,
					"ai" => PromptBuilder.From.assistant
				})
			).ToList();
			p.Insert(0, (system, PromptBuilder.From.system));
			pb.Messages = p.ToArray();
			return pb;
		}
	}
}
