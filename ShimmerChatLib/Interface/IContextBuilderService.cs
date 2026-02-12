using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using System.Collections.Generic;

namespace ShimmerChatLib.Interface
{
    public interface IContextBuilderService
    {
        /// <summary>
        /// 为聊天构建PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <returns>构建好的PromptBuilder</returns>
        PromptBuilder BuildPromptBuilder(Chat chat, Agent agent);
        
        /// <summary>
        /// 为聊天构建包含工具定义的PromptBuilder
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agentDescription">代理描述</param>
        /// <param name="toolDefinitions">工具定义列表</param>
        /// <returns>构建好的PromptBuilder</returns>
        PromptBuilder BuildPromptBuilderWithTools(Chat chat, Agent agent, List<SharperLLM.FunctionCalling.Tool> toolDefinitions);

        PromptBuilder BuildPromptBuilderWithoutContextModify(Chat chat, Agent agent);

        /// <summary>
        /// 为继续生成构建PromptBuilder，对最后一条AI消息附加 prefix: true
        /// </summary>
        /// <param name="chat">聊天对象</param>
        /// <param name="agent">代理对象</param>
        /// <param name="toolDefinitions">工具定义列表</param>
        /// <param name="continuationMessage">需要继续的消息</param>
        /// <returns>构建好的PromptBuilder</returns>
        PromptBuilder BuildPromptBuilderForContinuation(Chat chat, Agent agent, List<SharperLLM.FunctionCalling.Tool> toolDefinitions, Message continuationMessage);
    }
}