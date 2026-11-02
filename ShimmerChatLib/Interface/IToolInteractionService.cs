using Newtonsoft.Json.Linq;
using ShimmerChatLib.Generation.ToolUi;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 工具 ↔ 用户 的现时交互通道。
    /// <para>
    /// 工具在 <c>ExecuteAsync</c> 内部调用 <see cref="RequestAsync"/> 并等待：等待期间本轮生成没有结束，
    /// 对话记录始终合法；用户回答后工具自己带着答案返回，loop 继续。等待默认无超时——
    /// 退出等待只有三条路：用户提交、用户跳过、生成被取消（此时 <see cref="RequestAsync"/> 抛
    /// <see cref="OperationCanceledException"/>）。
    /// </para>
    /// <para>
    /// 待回答记录只存在内存里（单例），因此页面刷新 / 切换对话后仍能继续回答；
    /// 进程重启会丢失待回答请求，但那同时意味着本次生成已经结束。
    /// </para>
    /// </summary>
    public interface IToolInteractionService
    {
        /// <summary>
        /// 请求用户输入并等待结果。
        /// </summary>
        /// <param name="chatGuid">所属对话；界面据此展示对应的请求。</param>
        /// <param name="request">交互描述（声明式表单或自定义视图）。</param>
        /// <param name="timeout">超时时间；null 表示无限等待。</param>
        /// <param name="ct">生成取消令牌，触发时必须抛 <see cref="OperationCanceledException"/>。</param>
        Task<ToolInteractionResult> RequestAsync(
            Guid chatGuid, ToolInteractionRequest request, TimeSpan? timeout, CancellationToken ct);

        /// <summary>指定对话当前待回答的交互（按创建时间排序）。</summary>
        IReadOnlyList<ToolInteractionPending> GetPending(Guid chatGuid);

        /// <summary>界面提交答案。<paramref name="answer"/> 会原样进入 tool_result。</summary>
        ToolInteractionSubmitOutcome Submit(Guid id, JObject? answer);

        /// <summary>界面跳过本次交互（"不等了，继续"）。</summary>
        ToolInteractionSubmitOutcome Skip(Guid id, string reason);

        /// <summary>取消指定对话的全部待回答交互（生成被停止 / 用户发了新消息）。返回受影响条数。</summary>
        int CancelAll(Guid chatGuid, string reason);

        /// <summary>待回答集合发生变化（新增 / 移除）。参数是对话 Guid；事件来自后台线程，订阅方须切回渲染线程。</summary>
        event Action<Guid>? PendingChanged;
    }
}
