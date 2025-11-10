using SharperLLM.API;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public interface ICompletionService
	{
		public Task<string> GetAIReply(Agent agent, Chat chat);
		public Task<ResponseEx> GetAIReplyReponseEx(Agent agent, Chat chat);
	}
}
