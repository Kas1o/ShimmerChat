using SharperLLM.API;
using ShimmerChatLib;
using System.Collections.Generic;

namespace ShimmerChat.Singletons
{
	public class CompletionServiceV1 : ICompletionService
	{
		private readonly IUserData UserData;
		private readonly IToolService ToolService;
		private readonly IContextBuilderService ContextBuilderService;

		public CompletionServiceV1(IUserData userData, IToolService toolService, IContextBuilderService contextBuilderService)
		{
			UserData = userData;
			ToolService = toolService;
			ContextBuilderService = contextBuilderService;
		}
		[Obsolete]
		public async Task<string> GetAIReply(Agent agent,Chat chat)
		{
			var reply = "";
			if (UserData.CompletionType == CompletionType.TextCompletion)
			{
				var templ = UserData.textCompletionSettings[UserData.CurrentTextCompletionSettingIndex].GetMessageTemplates();
				var promptBuilder = ContextBuilderService.BuildPromptBuilder(chat, agent);
				reply = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateText(new SharperLLM.Util.PromptBuilder(promptBuilder)
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
				var promptBuilder = ContextBuilderService.BuildPromptBuilder(chat, agent);
				reply = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatReply(promptBuilder);
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
				var promptBuilder = ContextBuilderService.BuildPromptBuilder(chat, agent);
				rsp = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatEx(promptBuilder);
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
				var toolDefinitions = ToolService.GetEnabledToolDefinitions().ToList();
				var promptBuilder = ContextBuilderService.BuildPromptBuilderWithTools(chat, agent, toolDefinitions);
				var rsp = await UserData.ApiSettings[UserData.CurrentAPISettingIndex].llmapi.GenerateChatEx(promptBuilder);

				// 通知调用方本轮AI回复
				onResponse(rsp);

				if (rsp.FinishReason != FinishReason.FunctionCall || rsp.Body.toolCalls == null || rsp.Body.toolCalls.Count == 0)
					break;

				foreach (ToolCall toolCall in rsp.Body.toolCalls)
				{
					string? toolResult = await ToolService.ExecuteToolAsync(toolCall.name, toolCall.arguments ?? "", chat, agent);
					ToolCallback((toolCall.name, toolResult ?? "[No result]", toolCall.id));
				}
			}
		}

	}
}
