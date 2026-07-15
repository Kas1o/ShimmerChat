namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 后生成节点接口。在 LLM 生成响应后执行，对原始响应文本进行过滤、转换、富化等处理。
    /// 继承 <see cref="ITreeNode"/>，可复用节点编辑器基础设施。
    /// </summary>
    public interface IPostGenerationNode : ITreeNode
    {
        /// <summary>
        /// 执行后处理逻辑，可修改 context.Env.ResponseText 或 SharedState。
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
    /// 后生成管线环境。包含 LLM 响应文本、前生成上下文片段和共享状态。
    /// </summary>
    public class PostGenerationEnv
    {
        /// <summary>LLM 原始响应文本（节点可修改）</summary>
        public string ResponseText { get; set; }

        /// <summary>前生成管线构建的 Fragments（只读参考）</summary>
        public IReadOnlyList<ContextSegment> PreFragments { get; }

        /// <summary>节点间共享状态</summary>
        public Dictionary<string, object> SharedState { get; } = new();

        /// <summary>持久化服务访问</summary>
        public PersistentEnv Persistent { get; }

        /// <summary>当前管线的序列化器（CallNode 加载预设时使用）</summary>
        public ITreeNodeSerializer Serializer { get; set; } = default!;

        public PostGenerationEnv(string responseText, IReadOnlyList<ContextSegment> preFragments, PersistentEnv persistent)
        {
            ResponseText = responseText;
            PreFragments = preFragments;
            Persistent = persistent;
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
