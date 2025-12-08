using SharperLLM.API;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using System.Collections.Generic;
using System.Xml.Linq;

namespace ShimmerChat.Singletons
{
	public class CompletionServiceV1 : ICompletionService
	{
		private readonly IToolService ToolService;
		private readonly IContextBuilderService ContextBuilderService;
		private readonly IKVDataService KVDataService;

		public CompletionServiceV1(IToolService toolService, IContextBuilderService contextBuilderService, IKVDataService kVData)
		{
			ToolService = toolService;
			ContextBuilderService = contextBuilderService;
			KVDataService = kVData;
		}

		List<ApiSetting> ApiSettings
		{
			get
			{
				var apisettings = KVDataService.Read("ApiSettings", "apiSetting") ?? "null";
				return Newtonsoft.Json.JsonConvert.DeserializeObject<List<ApiSetting>>(apisettings);
			}
		}

		int SelectedAPIIndex
		{
			get
			{
				var selectedIndex = KVDataService.Read("ApiSettings", "selectedAPIIndex") ?? "0";
				return int.Parse(selectedIndex);
			}
		}

		List<TextCompletionSetting>? textCompletionSettings
		{
			get
			{
				var tcs = KVDataService.Read("ApiSettings", "textCompletionSettings") ?? "null";
				return Newtonsoft.Json.JsonConvert.DeserializeObject<List<TextCompletionSetting>>(tcs);
			}
		}

		int SelectedTCS
		{
			get
			{
				var selectedTCS = KVDataService.Read("ApiSettings", "selectedTCS") ?? "0";
				return int.Parse(selectedTCS);
			}
		}

		CompletionType CompletionType
		{
			get
			{
				var completionType = KVDataService.Read("ApiSettings", "CompletionType") ?? ((int)CompletionType.ChatCompletion).ToString();
				return (CompletionType)Enum.Parse(typeof(CompletionType), completionType);
			}
			set
			{
				KVDataService.Write("ApiSettings", "CompletionType", ((int)value).ToString());
			}
		}

		[Obsolete]
		public async Task<string> GetAIReply(Agent agent,Chat chat)
		{
			var reply = "";
			if (CompletionType == CompletionType.TextCompletion)
			{
				var templ = textCompletionSettings[SelectedTCS].GetMessageTemplates();
				var promptBuilder = ContextBuilderService.BuildPromptBuilder(chat, agent);
				reply = await ApiSettings[SelectedAPIIndex].llmapi.GenerateText(new SharperLLM.Util.PromptBuilder(promptBuilder)
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
				reply = await ApiSettings[SelectedAPIIndex].llmapi.GenerateChatReply(promptBuilder);
			}

			return reply;
		}
		public async Task<ResponseEx> GetAIReplyReponseEx(Agent agent, Chat chat, Action<(string name, string resp)> ToolCallback)
		{
			ResponseEx rsp = null;
			if (CompletionType == CompletionType.TextCompletion)
			{
				throw new NotImplementedException("不支持在TextCompletion 中使用此API");
			}
			else
			{
				var promptBuilder = ContextBuilderService.BuildPromptBuilder(chat, agent);
				rsp = await ApiSettings[SelectedAPIIndex].llmapi.GenerateChatEx(promptBuilder);
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
			if (CompletionType == CompletionType.TextCompletion)
				throw new NotImplementedException("不支持在TextCompletion 中使用此API");

			while (true)
			{
				var toolDefinitions = ToolService.GetEnabledToolDefinitions().ToList();
				var promptBuilder = ContextBuilderService.BuildPromptBuilderWithTools(chat, agent, toolDefinitions);
				var rsp = await ApiSettings[SelectedAPIIndex].llmapi.GenerateChatEx(promptBuilder);

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
