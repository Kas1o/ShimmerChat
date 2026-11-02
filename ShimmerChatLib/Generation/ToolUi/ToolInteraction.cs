using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShimmerChatLib.Generation.ToolUi
{
    /// <summary>
    /// 工具向用户发起的一次交互请求。它在等待期间只存在于内存（<see cref="Interface.IToolInteractionService"/>），
    /// 不落库：等待是有界的、"现在发生的事"，不是对话历史的一部分。
    /// </summary>
    public sealed class ToolInteractionRequest
    {
        /// <summary>发起请求的工具名（用于诊断与默认标题）。</summary>
        public required string ToolName { get; init; }

        /// <summary>标题（通常就是问题本身）。</summary>
        public required string Title { get; init; }

        /// <summary>补充说明，可为空。</summary>
        public string? Message { get; init; }

        /// <summary>声明式表单。与 <see cref="ViewId"/> 都为空时，宿主会降级为"原始 JSON + 自由文本"。</summary>
        public ToolUiForm? Form { get; init; }

        /// <summary>自定义视图 Id（<c>[ToolUiView]</c> 声明的标识）；未注册时降级到 <see cref="Form"/>。</summary>
        public string? ViewId { get; init; }

        /// <summary>传给自定义视图的额外数据（可空）。</summary>
        public JObject? Payload { get; init; }
    }

    /// <summary>一次交互的最终结局。四路竞态（提交 / 跳过 / 超时 / 取消）里只有一路胜出。</summary>
    public enum ToolInteractionOutcome
    {
        /// <summary>用户提交了答案。</summary>
        Answered = 0,

        /// <summary>用户主动跳过（"不等了，继续"）。</summary>
        Skipped = 1,

        /// <summary>等待超时。</summary>
        TimedOut = 2,

        /// <summary>生成被取消（用户停止 / 宿主中止）。工具应以异常形式感知，通常看不到这个值。</summary>
        Cancelled = 3
    }

    /// <summary>
    /// 交互结果。<see cref="ToModelJson"/> 是唯一一处"给模型看的措辞"，
    /// 由宿主侧代码统一生成，界面与工具都不允许自行拼接。
    /// </summary>
    public sealed class ToolInteractionResult
    {
        public required ToolInteractionOutcome Outcome { get; init; }

        /// <summary>用户提交的答案（仅 <see cref="ToolInteractionOutcome.Answered"/>）。</summary>
        public JObject? Answer { get; init; }

        /// <summary>附加原因（跳过 / 取消时说明来由）。</summary>
        public string? Reason { get; init; }

        /// <summary>序列化成写进 tool_result 的文本。</summary>
        public string ToModelJson()
        {
            var obj = new JObject
            {
                ["status"] = Outcome switch
                {
                    ToolInteractionOutcome.Answered => "answered",
                    ToolInteractionOutcome.Skipped => "skipped",
                    ToolInteractionOutcome.TimedOut => "timed_out",
                    _ => "cancelled"
                }
            };

            if (Answer != null)
                obj["answer"] = Answer;

            if (!string.IsNullOrEmpty(Reason))
                obj["reason"] = Reason;

            return obj.ToString(Formatting.None);
        }
    }

    /// <summary>界面上看到的一条待回答交互。</summary>
    public sealed class ToolInteractionPending
    {
        public required Guid Id { get; init; }

        public required Guid ChatGuid { get; init; }

        public required ToolInteractionRequest Request { get; init; }

        public required DateTime CreatedAt { get; init; }

        /// <summary>超时时间；null 表示无限等待（默认）。</summary>
        public TimeSpan? Timeout { get; init; }
    }

    /// <summary>界面提交 / 跳过后的结果。失败必须给出可追踪错误码，不能静默。</summary>
    public sealed record ToolInteractionSubmitOutcome(bool Success, string? Code, string? Message)
    {
        public static ToolInteractionSubmitOutcome Ok() => new(true, null, null);

        public static ToolInteractionSubmitOutcome Fail(string code, string message) => new(false, code, message);
    }

    /// <summary>界面提交交互结果时的错误码。</summary>
    public static class ToolInteractionSubmitCodes
    {
        /// <summary>
        /// 该交互已经结束：已被回答 / 被跳过 / 已超时。
        /// 记录在等待结束时就已移除，因此这里无法、也不打算区分具体是哪一种。
        /// </summary>
        public const string AlreadyResolved = "TOOL_INTERACTION_ALREADY_RESOLVED";

        /// <summary>交互记录已不存在（已清理）。</summary>
        public const string NotFound = "TOOL_INTERACTION_NOT_FOUND";
    }
}
