using System;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 预生成树中的业务节点接口。每个节点负责在 ExecuteAsync 中修改 PreGenerationEnv。
    /// 树结构状态（Id / Name / 展开状态）由 <see cref="PreGenerationNodeWrapper"/> 持有。
    /// </summary>
    public interface IPreGenerationNode
    {
        /// <summary>
        /// 执行节点逻辑，修改 context.Env。
        /// 返回 NodeResult，成功时 Success=true，失败时通过 Code/Message/Details 描述错误。
        /// </summary>
        Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context);
    }

    /// <summary>
    /// 预生成节点执行上下文
    /// </summary>
    public class PreNodeExecutionContext
    {
        public PreGenerationEnv Env { get; }
        public CancellationToken CancellationToken { get; }

        /// <summary>当前执行节点（包装器）的 Id，由包装器在执行期间设置。</summary>
        public string? CurrentNodeId { get; internal set; }

        /// <summary>当前执行节点（包装器）的显示名，由包装器在执行期间设置。</summary>
        public string? CurrentNodeName { get; internal set; }

        public PreNodeExecutionContext(PreGenerationEnv env, CancellationToken ct = default)
        {
            Env = env;
            CancellationToken = ct;
        }
    }
}
