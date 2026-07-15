using System;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 预生成树中的节点接口。每个节点负责在 ExecuteAsync 中修改 PreGenerationEnv。
    /// 继承 <see cref="ITreeNode"/>，使节点编辑器可统一操作。
    /// </summary>
    public interface IPreGenerationNode : ITreeNode
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

        public PreNodeExecutionContext(PreGenerationEnv env, CancellationToken ct = default)
        {
            Env = env;
            CancellationToken = ct;
        }
    }
}
