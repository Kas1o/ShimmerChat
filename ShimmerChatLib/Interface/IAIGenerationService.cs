using SharperLLM.API;
using ShimmerChatLib;
using System.Threading;

namespace ShimmerChatLib.Interface
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
        
        /// <summary>
        /// 流式生成AI响应的方法
        /// </summary>
        /// <param name="agent">代理对象</param>
        /// <param name="chat">聊天对象</param>
        /// <param name="handleStreamResponses">流式响应处理函数，接收响应流并处理</param>
        /// <param name="onToolCall">工具调用回调函数</param>
        /// <param name="onToolResult">工具结果回调函数</param>
        /// <param name="cancellationToken">取消令牌，用于取消生成过程</param>
        /// <returns>Task</returns>
        Task GenerateAIResponseStreamAsync(
            Agent agent,
            Chat chat,
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStreamResponses,
            Action<List<SharperLLM.API.ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken);
    }
}