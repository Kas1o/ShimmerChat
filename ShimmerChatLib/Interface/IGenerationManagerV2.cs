using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 生成管道管理器接口。
    /// 执行前生成树 → 构建 Prompt → 调用 API → 轮次循环（后处理树决定是否继续）。
    /// </summary>
    public interface IGenerationManagerV2
    {
        /// <summary>
        /// 流式生成 AI 响应
        /// </summary>
        /// <param name="onMessagesCommitted">
        /// 后处理节点在本轮追加的消息（已写入 Chat 与存储）。宿主用它同步插件面板与滚动。
        /// </param>
        Task GenerateStreamAsync(
            Agent agent,
            Chat chat,
            Func<ResponseEx, Task> onStreamDelta,
            Func<ResponseEx, Task> onAssistantComplete,
            Func<IReadOnlyList<Message>, Task>? onMessagesCommitted = null,
            Func<Task>? onPostGenerationStarted = null,
            PipelineTreeOverrides? overrides = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 非流式生成 AI 响应
        /// </summary>
        Task GenerateAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Func<IReadOnlyList<Message>, Task>? onMessagesCommitted = null);

        /// <summary>
        /// 构建生成环境：执行前生成树 + 加载历史消息。
        /// overrides 非空时用其树 JSON 替代 Agent 的前生成树（生成提供器场景）。
        /// </summary>
        Task<PreGenerationEnv> BuildEnvironment(
            Agent agent, Chat chat, CancellationToken ct,
            PipelineTreeOverrides? overrides = null);

        /// <summary>
        /// 后生成处理：执行后生成管线。节点直接操作对话与存储，返回值携带循环控制意图。
        /// </summary>
        Task<PostPipelineResult> PostProcessAsync(
            Agent agent,
            ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv,
            PostPipelineContext? roundContext = null,
            CancellationToken ct = default,
            PipelineTreeOverrides? overrides = null);
    }
}
