namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 工具执行结果形态。只有两种：预期内的成功，和预期内的失败。
    /// 意外（编程错误、未处理异常）走异常，由节点统一捕获并记录；
    /// "等待用户"不是结果形态——它是工具内部的一次有界等待，见 <see cref="Interface.IToolInteractionService"/>。
    /// </summary>
    public enum ToolResultStatus
    {
        /// <summary>执行完成。模型看到 <see cref="ToolResult.Content"/>。</summary>
        Completed = 0,

        /// <summary>执行失败（参数非法、服务不可用等预期内错误）。</summary>
        Failed = 1
    }

    /// <summary>
    /// 工具执行结果。<see cref="ToolResultStatus.Failed"/> 必须带可追踪的错误码，
    /// 与节点层的 <see cref="NodeResult"/> 语义保持一致。
    /// </summary>
    public sealed class ToolResult
    {
        public required ToolResultStatus Status { get; init; }

        /// <summary>写进 tool_result 的文本（<see cref="ToolResultStatus.Completed"/> 时有意义，允许为空串）。</summary>
        public string? Content { get; init; }

        /// <summary>失败错误码（<see cref="ToolErrorCodes"/> 中的常量）。</summary>
        public string? ErrorCode { get; init; }

        /// <summary>失败的用户/模型可读信息。</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>失败的技术细节，由节点写进 DebugOutput，不塞给模型。</summary>
        public string? Details { get; init; }

        public static ToolResult Ok(string? content = null) => new()
        {
            Status = ToolResultStatus.Completed,
            Content = content ?? string.Empty
        };

        public static ToolResult Fail(string code, string message, string? details = null) => new()
        {
            Status = ToolResultStatus.Failed,
            ErrorCode = code,
            ErrorMessage = message,
            Details = details
        };
    }

    /// <summary>
    /// 工具错误码。统一 TOOL_ 前缀，便于在 DebugOutput 里检索。
    /// </summary>
    public static class ToolErrorCodes
    {
        /// <summary>参数缺失 / 类型不符 / JSON 无法解析。</summary>
        public const string InvalidArguments = "TOOL_INVALID_ARGUMENTS";

        /// <summary>当前上下文无法展示交互界面（如子代理虚拟会话）。</summary>
        public const string InteractionUnavailable = "TOOL_INTERACTION_UNAVAILABLE";

        /// <summary>本轮交互次数超过上限，防止模型循环提问。</summary>
        public const string InteractionLimitReached = "TOOL_INTERACTION_LIMIT";

        /// <summary>工具依赖的外部服务返回失败。</summary>
        public const string ServiceError = "TOOL_SERVICE_ERROR";

        /// <summary>工具声明的配置不可用。</summary>
        public const string ConfigInvalid = "TOOL_CONFIG_INVALID";
    }
}
