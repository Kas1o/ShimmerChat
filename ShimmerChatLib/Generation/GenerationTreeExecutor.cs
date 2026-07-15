using System.Text;

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
        /// <exception cref="InvalidOperationException">节点执行失败时抛出，异常信息包含失败节点和错误详情</exception>
        public async Task<PreGenerationEnv> ExecuteAsync(
            IPreGenerationNode rootNode,
            PersistentEnv env,
            CancellationToken ct = default)
        {
            var generationEnv = new PreGenerationEnv(env);
            var context = new PreNodeExecutionContext(generationEnv, ct);

            var result = await rootNode.ExecuteAsync(context);

            if (!result.Success)
            {
                var sb = new StringBuilder();
                sb.Append("Generation tree execution failed. ");
                sb.Append($"Node: '{result.NodeName ?? "?"}' (ID: {result.NodeId ?? "?"}). ");
                sb.Append($"Code: {result.Code ?? "?"}. ");
                sb.Append($"Message: {result.Message ?? "?"}");
                if (!string.IsNullOrEmpty(result.Details))
                {
                    sb.AppendLine();
                    sb.Append("Details: ");
                    sb.Append(result.Details);
                }
                throw new InvalidOperationException(sb.ToString());
            }

            return generationEnv;
        }
    }
}
