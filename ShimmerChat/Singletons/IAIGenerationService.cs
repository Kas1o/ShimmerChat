using SharperLLM.API;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
    public interface IAIGenerationService
    {
        /// <summary>
        /// 统一的AI生成方法，自动处理不同的Completion模式
        /// </summary>
        /// <param name="agent">代理对象</param>
        /// <param name="chat">聊天对象</param>
        /// <param name="onResponse">响应回调函数，用于UI更新</param>
        /// <param name="onToolResult">工具结果回调函数</param>
        /// <returns>Task</returns>
        Task GenerateAIResponseAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Action<(string name, string resp, string id)> onToolResult);
    }
}