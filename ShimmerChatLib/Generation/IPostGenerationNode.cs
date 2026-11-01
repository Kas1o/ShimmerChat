namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 后生成节点接口。在 LLM 生成响应后执行，可修改本轮响应、直接增删改对话消息，
    /// 并决定是否继续下一轮（<see cref="PostGenerationEnv.LoopDirective"/>）。
    /// 继承 <see cref="ITreeNode"/>，可复用节点编辑器基础设施。
    /// </summary>
    public interface IPostGenerationNode : ITreeNode
    {
        /// <summary>
        /// 执行后处理逻辑。可修改 <see cref="PostGenerationEnv.ResponseText"/>、
        /// 直接操作 <see cref="PostGenerationEnv.Chat"/> 与 <see cref="PostGenerationEnv.MessageStore"/>。
        /// </summary>
        Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context);
    }

    /// <summary>
    /// 后生成节点执行上下文
    /// </summary>
    public class PostNodeExecutionContext
    {
        public PostGenerationEnv Env { get; }
        public CancellationToken CancellationToken { get; }

        public PostNodeExecutionContext(PostGenerationEnv env, CancellationToken ct = default)
        {
            Env = env;
            CancellationToken = ct;
        }
    }

    /// <summary>
    /// 后生成节点执行结果
    /// </summary>
    public class PostNodeResult
    {
        public bool Success { get; }
        public string? Code { get; }
        public string? Message { get; }
        public string? Details { get; }
        public string? NodeId { get; set; }
        public string? NodeName { get; set; }

        private PostNodeResult(bool success, string? code, string? message, string? details)
        {
            Success = success;
            Code = code;
            Message = message;
            Details = details;
        }

        public static PostNodeResult SuccessResult() => new(true, null, null, null);

        public static PostNodeResult Failure(string code, string message, string? details = null)
            => new(false, code, message, details);
    }
}
