namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 生成树执行引擎。从根节点开始遍历执行整棵树。
    /// 遍历逻辑由各节点内部控制（SequenceNode 顺序执行子节点等）。
    /// </summary>
    public class GenerationTreeExecutor
    {
        /// <summary>
        /// 执行整棵生成树
        /// </summary>
        /// <param name="rootNode">根节点</param>
        /// <param name="env">持久化环境</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>执行后的生成环境</returns>
        public async Task<GenerationEnv> ExecuteAsync(
            IGenerationNode rootNode,
            PersistentEnv env,
            CancellationToken ct = default)
        {
            var generationEnv = new GenerationEnv(env);
            var context = new NodeExecutionContext(generationEnv, ct);

            await rootNode.ExecuteAsync(context);

            return generationEnv;
        }
    }
}
