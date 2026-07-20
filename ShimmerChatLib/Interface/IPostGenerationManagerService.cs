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
        /// 执行后生成节点树，对 LLM 响应消息进行变换处理。
        /// 可用于 FunctionCall 请求和最终 Stop 响应。
        /// </summary>
        /// <param name="agent">当前代理</param>
        /// <param name="responseMessage">LLM 原始响应消息（含 tool calls、thinking 等）</param>
        /// <param name="preFragments">前生成管线构建的 Fragments</param>
        /// <param name="persistentEnv">持久化环境</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的响应消息，失败时返回原始消息</returns>
        Task<ChatMessage> ExecuteAsync(Agent agent, ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv, CancellationToken ct = default);
    }
}
