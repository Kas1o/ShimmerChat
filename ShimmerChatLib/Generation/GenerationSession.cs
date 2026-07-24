using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 生成会话状态持有者。与页面生命周期解耦，离开页面后生成继续运行。
/// 会话持有 Chat 和 Agent 的引用，页面可通过 Chat.Messages 获取最新消息列表。
/// </summary>
public class GenerationSession
{
    /// <summary>关联的 Chat GUID</summary>
    public string ChatGuid { get; }

    /// <summary>关联的 Agent GUID</summary>
    public string AgentGuid { get; }

    /// <summary>会话是否正在活跃运行</summary>
    public bool IsActive { get; internal set; }

    /// <summary>当前生成阶段：pre / gen / post / null(已完成)</summary>
    public string? Phase { get; internal set; }

    /// <summary>会话启动时间</summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// 会话持有的 Chat 引用。活跃会话期间，消息会直接操作此 Chat。
    /// 页面可绑定 Chat.Messages 进行显示。
    /// </summary>
    public Chat? Chat { get; internal set; }

    /// <summary>
    /// 会话持有的 Agent 引用。
    /// </summary>
    public Agent? Agent { get; internal set; }

    /// <summary>当前正在生成的 AI 消息 ID（用于回调中查找消息）</summary>
    internal Guid GeneratingMessageId { get; set; }

    internal CancellationTokenSource Cts { get; private set; }
    internal Task? RunningTask { get; set; }

    /// <summary>UI 状态变更通知（页面调用 InvokeAsync(StateHasChanged)）</summary>
    public event Action? StateChanged;

    /// <summary>请求滚动到底部</summary>
    public event Action? ScrollRequested;

    /// <summary>错误通知（message, title）</summary>
    public event Action<string, string>? ErrorOccurred;

    /// <summary>AI 消息完成通知（用于 ChatPluginPanelContainer）</summary>
    public event Action<Message>? AgentMessageCompleted;

    /// <summary>工具结果通知（用于 ChatPluginPanelContainer）</summary>
    public event Action<Message>? ToolResultAdded;

    /// <summary>用户消息通知（用于 ChatPluginPanelContainer）</summary>
    public event Action<Message>? UserMessageAdded;

    /// <summary>生成完成通知（用于桌面通知等）</summary>
    public event Action? GenerationCompleted;

    internal GenerationSession(string chatGuid, string agentGuid)
    {
        ChatGuid = chatGuid;
        AgentGuid = agentGuid;
        StartedAt = DateTime.Now;
        Cts = new CancellationTokenSource();
    }

    public void Cancel()
    {
        try { Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal void NotifyStateChanged() => StateChanged?.Invoke();
    internal void NotifyScrollRequested() => ScrollRequested?.Invoke();
    internal void NotifyError(string message, string title) => ErrorOccurred?.Invoke(message, title);
    internal void NotifyAgentMessageCompleted(Message msg) => AgentMessageCompleted?.Invoke(msg);
    internal void NotifyToolResultAdded(Message msg) => ToolResultAdded?.Invoke(msg);
    internal void NotifyUserMessageAdded(Message msg) => UserMessageAdded?.Invoke(msg);
    internal void NotifyGenerationCompleted() => GenerationCompleted?.Invoke();

    internal void ResetCts()
    {
        Cts?.Dispose();
        Cts = new CancellationTokenSource();
    }

    internal void DisposeCts()
    {
        Cts?.Dispose();
        Cts = null!;
    }
}
