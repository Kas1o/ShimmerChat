using SharperLLM.API;
using ShimmerChatLib;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

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
        
        /// <summary>
        /// 创建一个累积响应的异步流
        /// </summary>
        private async IAsyncEnumerable<ResponseEx> GetAccumulatedResponseStream(
            IAsyncEnumerable<ResponseEx> originalStream,
            CancellationToken cancellationToken,
            Action<ResponseEx>? accumulateCallback = null)
        {
            ResponseEx accumulated = new ResponseEx { Body = new SharperLLM.Util.ChatMessage { Content = "" }, FinishReason = FinishReason.None };
            
            await foreach (var response in originalStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 累积响应内容
                accumulated += response;
                
                // 调用回调函数，传递原始响应
                accumulateCallback?.Invoke(response);
                
                // 产生累积后的响应
                yield return accumulated;
            }
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
                var response = new ResponseEx { Body= new SharperLLM.Util.ChatMessage { Content = reply }, FinishReason = SharperLLM.API.FinishReason.Stop };
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

        public async Task GenerateAIResponseStreamAsync(
            Agent agent,
            Chat chat,
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStreamResponses,
            Action<List<SharperLLM.API.ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken)
        {
            if (_userData.CompletionType == CompletionType.TextCompletion)
            {
				// 对于TextCompletion模式，直接使用流式API
				var templ = _userData.textCompletionSettings[_userData.CurrentTextCompletionSettingIndex].GetMessageTemplates();
                var promptBuilder = _contextBuilderService.BuildPromptBuilder(chat, agent.description);
                var responseStream = _completionService.GenerateTextStreamAsync(
                    promptBuilder,
                    templ.sys_start,
                    templ.sys_stop,
                    templ.user_start,
                    templ.user_stop,
                    templ.char_start,
                    templ.char_stop,
                    cancellationToken);
                
                async IAsyncEnumerable<ResponseEx> ConvertStringStreamToResponseExStream(IAsyncEnumerable<string> respStream, CancellationToken ct)
                {
                    await foreach(var chunk in respStream)
                    {
                        yield return new ResponseEx { Body = new SharperLLM.Util.ChatMessage { Content = chunk }, FinishReason = FinishReason.None};
                    }

                    yield return new ResponseEx { Body = new SharperLLM.Util.ChatMessage { Content = "" }, FinishReason = FinishReason.Stop };
                }
                // 将字符串流转换为ResponseEx流
                var responseExStream = GetAccumulatedResponseStream(
                    ConvertStringStreamToResponseExStream(responseStream, cancellationToken),
                    cancellationToken);
                
                // 让调用方处理流式响应
                await handleStreamResponses(responseExStream);
			}
            else
            {
                // 对于ChatCompletion模式，使用流式API
                await RunAIWithToolLoopStreamAsync(
                    agent, chat,
                    handleStreamResponses,
                    onToolCall,
                    onToolResult,
                    cancellationToken);
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

                if (rsp.FinishReason != FinishReason.FunctionCall || rsp.Body.toolCalls == null || rsp.Body.toolCalls.Count == 0)
                    break;

                foreach (ToolCall toolCall in rsp.Body.toolCalls)
                {
                    string? toolResult = await _toolService.ExecuteToolAsync(toolCall.name, toolCall.arguments ?? "");
                    ToolCallback((toolCall.name, toolResult ?? "[No result]", toolCall.id));
                }
            }
        }
        
        /// <summary>
        /// 流式版本的AI与ToolCall循环，支持取消操作
        /// </summary>
        private async Task RunAIWithToolLoopStreamAsync(
            Agent agent,
            Chat chat,
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStreamResponses,
            Action<List<SharperLLM.API.ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var toolDefinitions = _toolService.GetEnabledToolDefinitions().ToList();
                var promptBuilder = _contextBuilderService.BuildPromptBuilderWithTools(chat, agent.description, toolDefinitions);
                
                // 累积流式响应
                ResponseEx accumulatedResponse = new ResponseEx { Body = new SharperLLM.Util.ChatMessage { Content = "" }, FinishReason = FinishReason.None };
                
                try
                {
                    // 创建一个自定义的异步流，处理累积响应
                    var responseStream = GetAccumulatedResponseStream(
                        _completionService.GenerateChatExStreamAsync(promptBuilder, cancellationToken), 
                        cancellationToken,
                        r => accumulatedResponse += r); // 回调函数，在处理流时同时累积响应
                    
                    // 让调用方处理流式响应
                    await handleStreamResponses(responseStream);
                    
                    // 检查是否需要调用工具
                    bool hasToolCalls = accumulatedResponse.FinishReason == FinishReason.FunctionCall && 
                        accumulatedResponse.Body.toolCalls != null && 
                        accumulatedResponse.Body.toolCalls.Count > 0;
                    
                    if (hasToolCalls)
                    {
                        // 通知调用方有工具调用
                        onToolCall(accumulatedResponse.Body.toolCalls);
                        
                        // 执行工具调用
                        foreach (ToolCall toolCall in accumulatedResponse.Body.toolCalls)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string? toolResult = await _toolService.ExecuteToolAsync(toolCall.name, toolCall.arguments ?? "");
                            onToolResult((toolCall.name, toolResult ?? "[No result]", toolCall.id));
                        }
                        // 继续循环，获取下一轮AI响应
                    }
                    else
                    {
                        // 如果不是工具调用，结束循环
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消操作时，不需要特殊处理，调用方已经从流中收到了累积的内容
                    throw;
                }
            }
        }
    }
}