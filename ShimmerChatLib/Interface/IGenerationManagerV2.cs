using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 生成管道管理器接口。
    /// 执行修改器树 → 构建 Prompt → 调用 API → Tool Call 循环。
    /// </summary>
    public interface IGenerationManagerV2
    {
        /// <summary>
        /// 流式生成 AI 响应
        /// </summary>
        Task GenerateStreamAsync(
            Agent agent,
            Chat chat,
            Func<ResponseEx, Task> onStreamDelta,
            Func<ResponseEx, Task> onAssistantComplete,
            Action<List<ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken);

        /// <summary>
        /// 非流式生成 AI 响应
        /// </summary>
        Task GenerateAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Action<(string name, string resp, string id)> onToolResult);

        /// <summary>
        /// 构建生成环境：执行修改器树 + 加载历史消息
        /// </summary>
        Task<GenerationEnv> BuildEnvironment(
            Agent agent, Chat chat, CancellationToken ct);
    }
}
