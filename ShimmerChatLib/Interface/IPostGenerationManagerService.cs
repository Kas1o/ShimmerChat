using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 后生成管线管理器。负责执行 Agent 的 PostGenerationTreeJson 节点树。
    /// </summary>
    public interface IPostGenerationManagerService
    {
        /// <summary>
        /// 执行后生成节点树。节点直接操作 <paramref name="persistentEnv"/> 中的 Chat 与消息存储，
        /// 返回值只携带本轮响应消息与循环控制意图。
        /// 可用于每一轮 assistant 响应（含工具调用轮与最终 Stop 轮）。
        /// </summary>
        /// <param name="agent">当前代理</param>
        /// <param name="responseMessage">LLM 原始响应消息（含 tool calls、thinking 等）</param>
        /// <param name="preFragments">前生成管线构建的 Fragments</param>
        /// <param name="persistentEnv">持久化环境</param>
        /// <param name="roundContext">本轮上下文（工具集、本轮消息、轮次、跨轮状态）</param>
        /// <param name="ct">取消令牌</param>
        /// <param name="overrides">管线树覆盖（生成提供器场景），非空时替代 Agent 的后生成树</param>
        Task<PostPipelineResult> ExecuteAsync(
            Agent agent,
            ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv,
            PostPipelineContext? roundContext = null,
            CancellationToken ct = default,
            PipelineTreeOverrides? overrides = null);
    }
}
