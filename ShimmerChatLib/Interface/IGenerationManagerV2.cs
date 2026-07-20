using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
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
            Func<Task>? onPostGenerationStarted = null,
            CancellationToken cancellationToken = default);

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
        Task<PreGenerationEnv> BuildEnvironment(
            Agent agent, Chat chat, CancellationToken ct);

        /// <summary>
        /// 后生成处理：执行后生成管线对 LLM 响应消息进行变换。
        /// 应在每次 assistant 完成时调用（含 FunctionCall 和 Stop）。
        /// </summary>
        Task<ChatMessage> PostProcessAsync(
            Agent agent, ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv, CancellationToken ct);
    }
}
