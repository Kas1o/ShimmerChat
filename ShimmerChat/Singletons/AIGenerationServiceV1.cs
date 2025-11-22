using SharperLLM.API;
using ShimmerChatLib;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ShimmerChat.Singletons
{
    public class AIGenerationServiceV1 : IAIGenerationService
    {
        private readonly ICompletionServiceV2 _completionService;
        private readonly IContextBuilderService _contextBuilderService;
        private readonly IUserData _userData;
        private readonly IToolService _toolService;

        public AIGenerationServiceV1(
            ICompletionServiceV2 completionService,
            IContextBuilderService contextBuilderService,
            IUserData userData,
            IToolService toolService)
        {
            _completionService = completionService;
            _contextBuilderService = contextBuilderService;
            _userData = userData;
            _toolService = toolService;
        }

        public async Task GenerateAIResponseAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Action<(string name, string resp, string id)> onToolResult)
        {
            if (_userData.CompletionType == CompletionType.TextCompletion)
            {
                // 处理TextCompletion模式
                var templ = _userData.textCompletionSettings[_userData.CurrentTextCompletionSettingIndex].GetMessageTemplates();
                var promptBuilder = _contextBuilderService.BuildPromptBuilder(chat, agent.description);
                string reply = await _completionService.GenerateTextAsync(
                    promptBuilder,
                    templ.sys_start,
                    templ.sys_stop,
                    templ.user_start,
                    templ.user_stop,
                    templ.char_start,
                    templ.char_stop);
                // 创建ResponseEx对象并调用回调
                var response = new ResponseEx { content = reply, FinishReason = SharperLLM.API.FinishReason.Stop };
                onResponse(response);
            }
            else
            {
                // 处理ChatCompletion模式（包含工具调用循环）
                await RunAIWithToolLoopAsync(
                    agent, chat,
                    onResponse,
                    onToolResult);
            }
        }

        /// <summary>
        /// 自动循环AI与ToolCall，直到FinishReason为Stop，每轮通过onResponse回调通知调用方
        /// </summary>
        private async Task RunAIWithToolLoopAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Action<(string name, string resp, string id)> ToolCallback)
        {
            while (true)
            {
                var toolDefinitions = _toolService.GetEnabledToolDefinitions().ToList();
                var promptBuilder = _contextBuilderService.BuildPromptBuilderWithTools(chat, agent.description, toolDefinitions);
                var rsp = await _completionService.GenerateChatExAsync(promptBuilder);

                // 通知调用方本轮AI回复
                onResponse(rsp);

                if (rsp.FinishReason != FinishReason.FunctionCall || rsp.toolCallings == null || rsp.toolCallings.Count == 0)
                    break;

                foreach (ToolCall toolCall in rsp.toolCallings)
                {
                    string? toolResult = await _toolService.ExecuteToolAsync(toolCall.name, toolCall.arguments ?? "");
                    ToolCallback((toolCall.name, toolResult ?? "[No result]", toolCall.id));
                }
            }
        }
    }
}