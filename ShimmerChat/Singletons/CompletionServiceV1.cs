using SharperLLM.API;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public class CompletionServiceV1 : ICompletionService
	{
		private IUserData UserData;
		public CompletionServiceV1(IUserData userData)
		{
			UserData = userData;
		}

		public async Task<string> GetAIReply(Agent agent,Chat chat)
		{
			var reply = "";
			if (UserData.CompletionType == CompletionType.TextCompletion)
			{
				var templ = UserData.textCompletionSettings[UserData.CurrentTextCompletionSettingIndex].GetMessageTemplates();
				reply = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateText(new SharperLLM.Util.PromptBuilder(chat.ToPromptBuilder(agent.description))
				{
					SysSeqPrefix = templ.sys_start,
					SysSeqSuffix = templ.sys_stop,
					InputPrefix = templ.user_start,
					InputSuffix = templ.user_stop,
					OutputPrefix = templ.char_start,
					OutputSuffix = templ.char_stop,
				}.GeneratePromptWithLatestOuputPrefix());
			}
			else
			{
				reply = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatReply(new SharperLLM.Util.PromptBuilder(chat.ToPromptBuilder(agent.description)));
			}

			return reply;
		}
		public async Task<ResponseEx> GetAIReplyReponseEx(Agent agent, Chat chat)
		{
			ResponseEx rsp = null;
			if (UserData.CompletionType == CompletionType.TextCompletion)
			{
				throw new NotImplementedException("不支持在TextCompletion 中使用此API");
			}
			else
			{
				rsp = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatEx(new SharperLLM.Util.PromptBuilder(chat.ToPromptBuilder(agent.description)));
			}

			return rsp;
		}
	}
}
