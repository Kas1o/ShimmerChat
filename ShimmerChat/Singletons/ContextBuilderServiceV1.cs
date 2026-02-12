using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using System.Collections.Generic;
using System.Linq;
using ShimmerChatLib.Models;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class ContextBuilderServiceV1 : IContextBuilderService
    {
        private readonly IContextModifierService _contextModifierService;

        public ContextBuilderServiceV1(IContextModifierService contextModifierService)
        {
            _contextModifierService = contextModifierService;
        }
        /// <summary>
        /// 将聊天转换为PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="system">系统提示</param>
        /// <param name="tools">可选的工具列表</param>
        /// <returns>构建好的PromptBuilder</returns>
        private PromptBuilder CreatePromptBuilder(Chat chat, string system, List<Tool> tools = null)
        {
            var pb = new PromptBuilder();
            var p = chat.Messages
                .Where(x => x.GenerationState != MessageGenerationState.Regenerating)
                .Select(
                    x =>
                        x.sender.ToLower() switch
                        {
                            Sender.User => (x.message, PromptBuilder.From.user),
                            Sender.System => (x.message, PromptBuilder.From.system),
                            Sender.AI => (x.message, PromptBuilder.From.assistant),
                            Sender.ToolResult => (x.message, PromptBuilder.From.tool_result),
                            var n => throw new InvalidOperationException($"Unsupported sender Type: {n}")
                        }
                ).ToList();
            p.Insert(0, (system, PromptBuilder.From.system));
            pb.Messages = p.ToArray();
            if(tools != null)
            {
                pb.AvailableTools = tools;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }
            return pb;
        }

        /// <summary>
        /// 为聊天构建PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <returns>构建好的PromptBuilder</returns>
        public PromptBuilder BuildPromptBuilder(Chat chat, Agent agent)
        {
            var promptBuilder = CreatePromptBuilder(chat, agent.description);
            // 获取最新的用户消息作为输入
            var latestUserMessage = chat.Messages.LastOrDefault(m => m.sender.ToLower() == Sender.User)?.message ?? string.Empty;
            // 应用上下文修改器
            _contextModifierService.ApplyModifiers(promptBuilder, chat, agent);
            return promptBuilder;
        }
        
        /// <summary>
        /// 为聊天构建包含工具定义的PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <param name="toolDefinitions">工具定义列表</param>
        /// <returns>构建好的PromptBuilder</returns>
        public PromptBuilder BuildPromptBuilderWithTools(Chat chat, Agent agent, List<Tool> toolDefinitions)
        {
            var promptBuilder = CreatePromptBuilder(chat, agent.description, toolDefinitions);
            // 获取最新的用户消息作为输入
            var latestUserMessage = chat.Messages.LastOrDefault(m => m.sender.ToLower() == Sender.User)?.message ?? string.Empty;
            // 应用上下文修改器
            _contextModifierService.ApplyModifiers(promptBuilder, chat, agent);
            return promptBuilder;
        }

        public PromptBuilder BuildPromptBuilderWithoutContextModify(Chat chat, Agent agent)
        {
            return CreatePromptBuilder(chat, agent.description);
        }

        /// <summary>
        /// 为继续生成构建PromptBuilder，对最后一条AI消息附加 prefix: true
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agent">代理对象</param>
        /// <param name="toolDefinitions">工具定义列表</param>
        /// <param name="continuationMessage">需要继续的消息</param>
        /// <returns>构建好的PromptBuilder</returns>
        public PromptBuilder BuildPromptBuilderForContinuation(Chat chat, Agent agent, List<Tool> toolDefinitions, Message continuationMessage)
        {
            var pb = new PromptBuilder();
            var p = chat.Messages
                .Where(x => x.GenerationState != MessageGenerationState.Regenerating)
                .Select(
                    x =>
                    {
                        var from = x.sender.ToLower() switch
                        {
                            Sender.User => PromptBuilder.From.user,
                            Sender.System => PromptBuilder.From.system,
                            Sender.AI => PromptBuilder.From.assistant,
                            Sender.ToolResult => PromptBuilder.From.tool_result,
                            var n => throw new InvalidOperationException($"Unsupported sender Type: {n}")
                        };

                        // 如果是需要继续的消息，附加 prefix: true
                        var message = x.message;
                        if (x == continuationMessage && x.sender == Sender.AI)
                        {
                            message = new ChatMessage
                            {
                                Content = x.message?.Content ?? string.Empty,
                                thinking = null,
                                toolCalls = x.message?.toolCalls,
                                CustomProperties = new Dictionary<string, object>(x.message?.CustomProperties ?? new Dictionary<string, object>())
                            };
                            message.CustomProperties["prefix"] = true;
                        }

                        return (message, from);
                    }
                ).ToList();
            p.Insert(0, (agent.description, PromptBuilder.From.system));
            pb.Messages = p.ToArray();
            if(toolDefinitions != null)
            {
                pb.AvailableTools = toolDefinitions;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            // 应用上下文修改器
            _contextModifierService.ApplyModifiers(pb, chat, agent);
            return pb;
        }
	}
}