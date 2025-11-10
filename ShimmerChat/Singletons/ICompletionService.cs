using SharperLLM.API;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public interface ICompletionService
	{
		public Task<string> GetAIReply(Agent agent, Chat chat);
		public Task<ResponseEx> GetAIReplyReponseEx(Agent agent, Chat chat, Action<(string name, string resp)> ToolCallback);
		public Task RunAIWithToolLoopAsync(
			Agent agent,
			Chat chat,
			Action<ResponseEx> onResponse,
			Action<(string name, string resp, string id)> ToolCallback);
	}
}
