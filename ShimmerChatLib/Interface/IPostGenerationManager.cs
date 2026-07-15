using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 后生成管线管理器。负责执行 Agent 的 PostGenerationTreeJson 节点树。
    /// </summary>
    public interface IPostGenerationManager
    {
        /// <summary>
        /// 执行后生成节点树，对 LLM 响应文本进行变换处理。
        /// </summary>
        /// <param name="agent">当前代理</param>
        /// <param name="responseText">LLM 原始响应文本</param>
        /// <param name="preFragments">前生成管线构建的 Fragments</param>
        /// <param name="persistentEnv">持久化环境</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的响应文本，失败时返回原始文本</returns>
        Task<string> ExecuteAsync(Agent agent, string responseText,
            IReadOnlyList<ContextSegment> preFragments,
            PersistentEnv persistentEnv, CancellationToken ct = default);
    }
}
