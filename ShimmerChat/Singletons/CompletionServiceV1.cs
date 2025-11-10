using SharperLLM.API;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public class CompletionServiceV1 : ICompletionService
	{
		private readonly IUserData UserData;
		private readonly IToolService ToolService;

		public CompletionServiceV1(IUserData userData, IToolService toolService)
		{
			UserData = userData;
			ToolService = toolService;
		}
		[Obsolete]
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
		public async Task<ResponseEx> GetAIReplyReponseEx(Agent agent, Chat chat, Action<(string name, string resp)> ToolCallback)
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
		/// <summary>
		/// 自动循环AI与ToolCall，直到FinishReason为Stop，每轮通过onResponse回调通知调用方。
		/// </summary>
		public async Task RunAIWithToolLoopAsync(
			Agent agent,
			Chat chat,
			Action<ResponseEx> onResponse,
			Action<(string name, string resp, string id)> ToolCallback)
		{
			if (UserData.CompletionType == CompletionType.TextCompletion)
				throw new NotImplementedException("不支持在TextCompletion 中使用此API");

			while (true)
			{
				var promptBuilder = chat.ToPromptBuilder(agent.description, ToolService.GetEnabledToolDefinitions().ToList());
				var rsp = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatEx(promptBuilder);

				// 通知调用方本轮AI回复
				onResponse(rsp);

				if (rsp.FinishReason != FinishReason.FunctionCall || rsp.toolCallings == null || rsp.toolCallings.Count == 0)
					break;

				foreach (ToolCall toolCall in rsp.toolCallings)
				{
					string? toolResult = await ToolService.ExecuteToolAsync(toolCall.name, toolCall.arguments ?? "");
					ToolCallback((toolCall.name, toolResult ?? "[No result]", toolCall.id));
				}
			}
		}

	}
}
