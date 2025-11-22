using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using System.Collections.Generic;

namespace ShimmerChat.Singletons
{
    public class ContextBuilderServiceV1 : IContextBuilderService
    {
        /// <summary>
        /// 为聊天构建PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <returns>构建好的PromptBuilder</returns>
        public PromptBuilder BuildPromptBuilder(Chat chat, string agentDescription)
        {
            return chat.ToPromptBuilder(agentDescription);
        }
        
        /// <summary>
        /// 为聊天构建包含工具定义的PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <param name="toolDefinitions">工具定义列表</param>
        /// <returns>构建好的PromptBuilder</returns>
        public PromptBuilder BuildPromptBuilderWithTools(Chat chat, string agentDescription, List<Tool> toolDefinitions)
        {
            return chat.ToPromptBuilder(agentDescription, toolDefinitions);
        }
    }
}