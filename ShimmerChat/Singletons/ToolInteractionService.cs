using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShimmerChatLib.Generation.ToolUi;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons;

/// <summary>
/// <see cref="IToolInteractionService"/> 的实现：单例 + 内存 pending 字典 + TCS。
/// <para>
/// 不落库、不写消息、不碰消息存储——交互只是"工具执行过程中的一次等待"，
/// 结局直接作为工具结果由执行节点写回对话。因此这里没有状态机，也没有恢复逻辑。
/// </para>
/// </summary>
public sealed class ToolInteractionService : IToolInteractionService
{
    private sealed class Entry
    {
        public required ToolInteractionPending Pending { get; init; }

        public required TaskCompletionSource<ToolInteractionResult> Completion { get; init; }
    }

    private readonly ConcurrentDictionary<Guid, Entry> _pending = new();
    private readonly ILogger<ToolInteractionService> _logger;

    public ToolInteractionService(ILogger<ToolInteractionService> logger)
    {
        _logger = logger;
    }

    public event Action<Guid>? PendingChanged;

    public async Task<ToolInteractionResult> RequestAsync(
        Guid chatGuid, ToolInteractionRequest request, TimeSpan? timeout, CancellationToken ct)
    {
        // TCS 必须异步延续：否则界面的 Submit 会在渲染线程上把工具剩余逻辑内联跑完，
        // 一旦工具在返回前又做重活，就等于把渲染线程拖进工具执行里。
        var completion = new TaskCompletionSource<ToolInteractionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = new ToolInteractionPending
        {
            Id = Guid.NewGuid(),
            ChatGuid = chatGuid,
            Request = request,
            CreatedAt = DateTime.Now,
            Timeout = timeout
        };

        _pending[pending.Id] = new Entry { Pending = pending, Completion = completion };
        RaiseChanged(chatGuid);

        try
        {
            if (timeout is { } limit)
            {
                using var timeoutCts = new CancellationTokenSource(limit);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    return await completion.Task.WaitAsync(linked.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    // 超时只是"等待者放弃"，TCS 本身仍然是未完成态，必须自己定案，
                    // 否则用户在 T+1ms 提交会"成功"写进一个早已结束的交互。
                    var timedOut = new ToolInteractionResult { Outcome = ToolInteractionOutcome.TimedOut };
                    return completion.TrySetResult(timedOut) ? timedOut : await completion.Task;
                }
            }

            // 无限等待（默认）：唯一的出路是提交 / 跳过 / 取消。
            return await completion.Task.WaitAsync(ct);
        }
        finally
        {
            _pending.TryRemove(pending.Id, out _);
            RaiseChanged(chatGuid);
        }
    }

    public IReadOnlyList<ToolInteractionPending> GetPending(Guid chatGuid)
    {
        return _pending.Values
            .Where(e => e.Pending.ChatGuid == chatGuid)
            .Select(e => e.Pending)
            .OrderBy(p => p.CreatedAt)
            .ToList();
    }

    public ToolInteractionSubmitOutcome Submit(Guid id, JObject? answer)
        => Resolve(id, new ToolInteractionResult { Outcome = ToolInteractionOutcome.Answered, Answer = answer });

    public ToolInteractionSubmitOutcome Skip(Guid id, string reason)
        => Resolve(id, new ToolInteractionResult { Outcome = ToolInteractionOutcome.Skipped, Reason = reason });

    public int CancelAll(Guid chatGuid, string reason)
    {
        var affected = 0;
        foreach (var entry in _pending.Values.Where(e => e.Pending.ChatGuid == chatGuid))
        {
            if (entry.Completion.TrySetResult(
                    new ToolInteractionResult { Outcome = ToolInteractionOutcome.Cancelled, Reason = reason }))
            {
                affected++;
                _logger.LogInformation(
                    "[ToolInteraction] 取消待回答交互 {InteractionId}（对话 {ChatGuid}）：{Reason}",
                    entry.Pending.Id, chatGuid, reason);
            }
        }

        return affected;
    }

    private ToolInteractionSubmitOutcome Resolve(Guid id, ToolInteractionResult result)
    {
        if (!_pending.TryGetValue(id, out var entry))
        {
            _logger.LogWarning("[ToolInteraction] 提交了不存在的交互 {InteractionId}。", id);
            return ToolInteractionSubmitOutcome.Fail(
                ToolInteractionSubmitCodes.NotFound,
                "This interaction no longer exists.");
        }

        if (entry.Completion.TrySetResult(result))
            return ToolInteractionSubmitOutcome.Ok();

        // 已经有人先定案（另一个窗口回答 / 跳过 / 超时）：不静默，明确告诉界面。
        return ToolInteractionSubmitOutcome.Fail(
            ToolInteractionSubmitCodes.AlreadyResolved,
            "This interaction has already ended.");
    }

    private void RaiseChanged(Guid chatGuid)
    {
        var handler = PendingChanged;
        if (handler == null) return;

        try
        {
            handler(chatGuid);
        }
        catch (Exception ex)
        {
            // 订阅方（界面）异常不能反过来打断工具执行
            _logger.LogError(ex, "[ToolInteraction] 通知界面刷新时出错（对话 {ChatGuid}）。", chatGuid);
        }
    }
}
