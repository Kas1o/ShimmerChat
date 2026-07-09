namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 生成树中的节点接口。每个节点负责在 ExecuteAsync 中修改 GenerationEnv。
    /// </summary>
    public interface IGenerationNode
    {
        /// <summary>
        /// 节点唯一标识
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 节点显示名称
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 执行节点逻辑，修改 context.Env
        /// </summary>
        Task ExecuteAsync(NodeExecutionContext context);
    }

    /// <summary>
    /// 节点执行上下文
    /// </summary>
    public class NodeExecutionContext
    {
        public GenerationEnv Env { get; }
        public CancellationToken CancellationToken { get; }

        public NodeExecutionContext(GenerationEnv env, CancellationToken ct = default)
        {
            Env = env;
            CancellationToken = ct;
        }
    }
}
