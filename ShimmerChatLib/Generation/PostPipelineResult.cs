using SharperLLM.Util;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 一轮后处理的调用参数。承载节点需要的回合上下文。
    /// </summary>
    public sealed class PostPipelineContext
    {
        /// <summary>本轮可用工具。</summary>
        public IReadOnlyList<IToolV2> Tools { get; init; } = Array.Empty<IToolV2>();

        /// <summary>本轮 assistant 的 Message 活对象。</summary>
        public Message? CurrentMessage { get; init; }

        /// <summary>本轮序号（0-based）。</summary>
        public int RoundIndex { get; init; }

        /// <summary>跨轮复用的共享状态字典；null 时管线内部新建。</summary>
        public Dictionary<string, object>? SharedState { get; init; }
    }

    /// <summary>
    /// 一轮后处理的执行结果。消息本身不在这里——
    /// 节点已经直接写进 <see cref="Chat"/> 与消息存储。
    /// </summary>
    public sealed class PostPipelineResult
    {
        /// <summary>后处理树是否成功执行。</summary>
        public bool Success { get; init; }

        /// <summary>失败错误码（见 <see cref="NodeErrorCodes"/>）。</summary>
        public string? Code { get; init; }

        /// <summary>失败时可读信息。</summary>
        public string? Message { get; init; }

        /// <summary>处理后（或原始）的响应消息，宿主用它更新本轮消息内容。</summary>
        public required ChatMessage ResponseMessage { get; init; }

        /// <summary>循环控制意图。</summary>
        public PostLoopDirective LoopDirective { get; init; } = PostLoopDirective.Stop;

        /// <summary>本轮 toolCalls 是否已被节点接管。</summary>
        public bool ToolCallsHandled { get; init; }

        /// <summary>节点是否改动过 Chat。</summary>
        public bool ChatMutated { get; init; }
    }
}
