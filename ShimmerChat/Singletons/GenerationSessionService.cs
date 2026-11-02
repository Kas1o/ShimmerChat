using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons;

/// <summary>
/// 后台生成会话服务。管理所有活跃的生成会话，与页面生命周期解耦。
/// 注册为 Singleton，离开页面后生成继续运行，返回时可恢复状态。
/// </summary>
public class GenerationSessionService
{
    private readonly ConcurrentDictionary<string, GenerationSession> _sessions = new();
    private readonly IGenerationManagerV2 _generationManager;
    private readonly IMessageStoreService _messageStore;
    private readonly IKVDataService _kvData;
    private readonly ILocService _loc;
    private readonly IToolInteractionService _toolInteractions;
    private readonly ILogger<GenerationSessionService> _logger;

    /// <summary>停止生成时等待后台任务收尾的上限。工具违约（不响应取消）不得拖住渲染线程。</summary>
    private static readonly TimeSpan StopWaitLimit = TimeSpan.FromSeconds(2);

    public GenerationSessionService(
        IGenerationManagerV2 generationManager,
        IMessageStoreService messageStore,
        IKVDataService kvData,
        ILocService loc,
        IToolInteractionService toolInteractions,
        ILogger<GenerationSessionService> logger)
    {
        _generationManager = generationManager;
        _messageStore = messageStore;
        _kvData = kvData;
        _loc = loc;
        _toolInteractions = toolInteractions;
        _logger = logger;
    }

    /// <summary>
    /// 获取指定 Chat 的活跃会话，不存在则返回 null。
    /// </summary>
    public GenerationSession? GetSession(string chatGuid)
    {
        _sessions.TryGetValue(chatGuid, out var session);
        return session;
    }

    /// <summary>
    /// 获取或创建会话。
    /// </summary>
    public GenerationSession GetOrCreateSession(string chatGuid, string agentGuid)
    {
        return _sessions.GetOrAdd(chatGuid, _ => new GenerationSession(chatGuid, agentGuid));
    }

    /// <summary>
    /// 取消之前的生成并重置会话（用于发送新消息、重新生成等场景）。
    /// <para>
    /// 必须 async：工具可能正处于"等待用户输入"的无限等待中，同步阻塞会把渲染线程一起冻住，
    /// 而解冻它的按钮（提交交互）恰好排在同一个渲染线程上。等待有上限，超时只记录不抛出。
    /// </para>
    /// </summary>
    public async Task CancelPreviousGenerationAsync(string chatGuid)
    {
        if (!_sessions.TryGetValue(chatGuid, out var session) || !session.IsActive)
            return;

        // 先让等待中的工具感知取消，否则它们会一直等着一个不会来的答案
        CancelPendingInteractions(session, "generation_cancelled");

        session.Cancel();
        var stopped = true;
        if (session.RunningTask != null)
        {
            try
            {
                await session.RunningTask.WaitAsync(StopWaitLimit);
            }
            catch (TimeoutException)
            {
                stopped = false;
                // 工具违约（不响应取消）：记录并继续清理，绝不在此无限等待
                _logger.LogError(
                    "[GenerationSessionService] Session {ChatGuid} did not stop within {Seconds}s; " +
                    "a tool is ignoring cancellation. Continuing with cleanup, the task is left running.",
                    chatGuid, StopWaitLimit.TotalSeconds);
            }
            catch (Exception)
            {
                // 取消导致的异常是预期结果
            }
        }

        session.IsActive = false;
        session.Phase = null;
        if (stopped)
            session.DisposeCts();

        // 标记生成中的消息为已完成
        if (session.Chat != null)
        {
            foreach (var msg in session.Chat.Messages.Where(m => m.IsGenerating))
            {
                msg.GenerationState = MessageGenerationState.Completed;
                session.Chat.SaveMessage(_messageStore, msg);
            }
        }
    }

    /// <summary>
    /// 停止生成（用户点击停止按钮）。
    /// </summary>
    public async Task StopGeneration(string chatGuid)
    {
        if (!_sessions.TryGetValue(chatGuid, out var session) || !session.IsActive)
            return;

        CancelPendingInteractions(session, "generation_stopped");
        session.Cancel();
        var stopped = true;
        if (session.RunningTask != null)
        {
            try
            {
                await session.RunningTask.WaitAsync(StopWaitLimit);
            }
            catch (TimeoutException)
            {
                stopped = false;
                _logger.LogError(
                    "[GenerationSessionService] Session {ChatGuid} did not stop within {Seconds}s; " +
                    "a tool is ignoring cancellation. Continuing with cleanup, the task is left running.",
                    chatGuid, StopWaitLimit.TotalSeconds);
            }
            catch (Exception)
            {
                // 取消导致的异常是预期结果
            }
        }

        session.IsActive = false;
        session.Phase = null;
        if (stopped)
            session.DisposeCts();

        if (session.Chat != null)
        {
            foreach (var msg in session.Chat.Messages.Where(m => m.IsGenerating))
            {
                msg.GenerationState = MessageGenerationState.Completed;
                session.Chat.SaveMessage(_messageStore, msg);
            }
        }

        session.NotifyStateChanged();
    }

    /// <summary>
    /// 清理已完成的会话。
    /// </summary>
    public void CleanupSession(string chatGuid)
    {
        if (_sessions.TryGetValue(chatGuid, out var session) && !session.IsActive)
        {
            _sessions.TryRemove(chatGuid, out _);
        }
    }

    /// <summary>
    /// 取消该会话正在等待回答的工具交互。工具在 <c>ExecuteAsync</c> 内部等待，
    /// 不打断它们的话，取消令牌之外没有任何东西能唤醒它们。
    /// </summary>
    private void CancelPendingInteractions(GenerationSession session, string reason)
    {
        if (session.Chat is { } chat)
        {
            _toolInteractions.CancelAll(chat.Guid, reason);
            return;
        }

        if (Guid.TryParse(session.ChatGuid, out var chatId))
        {
            _toolInteractions.CancelAll(chatId, reason);
            return;
        }

        _logger.LogWarning(
            "[GenerationSessionService] ChatGuid '{ChatGuid}' is not a valid Guid; pending tool interactions were not cancelled.",
            session.ChatGuid);
    }

    // ---- 启动生成 ----
    /// <summary>
    /// 启动普通生成。会话持有 Chat/Agent 引用，消息操作直接作用在 Chat.Messages 上。
    /// overrides 非空时用其树 JSON 替代 Agent 的管线树（生成提供器场景）。
    /// externalCt 非空时与会话取消令牌链接：外部令牌取消（如提供器停止）会同时取消本次生成。
    /// </summary>
    public void StartGeneration(GenerationSession session, Agent agent, Chat chat,
        bool throwExceptionInsteadOfPopup = false, PipelineTreeOverrides? overrides = null,
        CancellationToken externalCt = default)
    {
        if (session.IsActive)
        {
            _logger.LogWarning("[GenerationSessionService] Session {ChatGuid} already active, ignoring duplicate start",
                session.ChatGuid);
            return;
        }

        session.Chat = chat;
        session.Agent = agent;
        session.ResetCts();
        session.LinkExternalToken(externalCt);
        session.IsActive = true;
        session.Phase = "pre";
        session.NotifyStateChanged();

        var ct = session.Cts.Token;
        session.RunningTask = Task.Run(() =>
            RunGenerationLoop(session, agent, chat, continuationMessage: null,
                throwExceptionInsteadOfPopup, overrides, ct));
    }

    /// <summary>
    /// 启动续写生成。
    /// </summary>
    public void StartContinuation(GenerationSession session, Agent agent, Chat chat,
        Message continuationMessage, bool throwExceptionInsteadOfPopup = false)
    {
        if (session.IsActive)
        {
            _logger.LogWarning("[GenerationSessionService] Session {ChatGuid} already active, ignoring duplicate start",
                session.ChatGuid);
            return;
        }

        session.Chat = chat;
        session.Agent = agent;
        session.ResetCts();
        session.IsActive = true;
        session.Phase = "pre";
        session.NotifyStateChanged();

        var ct = session.Cts.Token;
        session.RunningTask = Task.Run(() =>
            RunGenerationLoop(session, agent, chat, continuationMessage,
                throwExceptionInsteadOfPopup, overrides: null, ct));
    }

    // ---- 生成主循环 ----

    private async Task RunGenerationLoop(
        GenerationSession session,
        Agent agent,
        Chat chat,
        Message? continuationMessage,
        bool throwExceptionInsteadOfPopup,
        PipelineTreeOverrides? overrides,
        CancellationToken ct)
    {
        string? originalContent = continuationMessage?.CurrentVersion?.Content;

        // 每次开始新一轮生成都重新从持久化读取 Agent，避免长期驻留的对话页
        // 持有过期副本，导致在其它连接中编辑的节点树 / 后处理树不生效。
        agent = ReloadAgent(agent);
        session.Agent = agent;

        try
        {
            await _generationManager.GenerateStreamAsync(
                agent, chat,
                onStreamDelta: accumulated =>
                {
                    session.Phase = "gen";
                    HandleStreamDelta(session, chat, accumulated);
                    return Task.CompletedTask;
                },
                onAssistantComplete: accumulated =>
                {
                    session.Phase = null;
                    HandleAssistantComplete(session, chat, accumulated, originalContent, continuationMessage);
                    GetDirty(chat, agent);
                    session.NotifyStateChanged();
                    return Task.CompletedTask;
                },
                onMessagesCommitted: appended =>
                {
                    HandleCommittedMessages(session, chat, appended);
                    return Task.CompletedTask;
                },
                onPostGenerationStarted: () =>
                {
                    session.Phase = "post";
                    var msg = chat.Messages.FirstOrDefault(
                        m => m.GenerationState == MessageGenerationState.Generating);
                    if (msg != null)
                        msg.GenerationState = MessageGenerationState.PostProcessing;
                    session.NotifyStateChanged();
                    return Task.CompletedTask;
                },
                overrides: overrides,
                cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            session.Phase = null;
        }
        catch (Exception ex)
        {
            session.Phase = null;
            foreach (var msg in chat.Messages.Where(m => m.IsGenerating))
            {
                msg.GenerationState = MessageGenerationState.Completed;
            }

            _logger.LogError(ex.StackTrace);

            if (throwExceptionInsteadOfPopup)
                throw;

            session.NotifyError(
                $"{ex.Message} \n {ex.InnerException?.Message ?? ""}",
                _loc.Format("chat.gen_error", ex.Source ?? "[NULL]"));
        }
        finally
        {
            // 安全网：生成结束时不应残留待回答交互（异常、取消、正常结束都走这里）。
            _toolInteractions.CancelAll(chat.Guid, "generation_finished");

            session.IsActive = false;
            session.DisposeCts();
            GetDirty(chat, agent);
            session.NotifyStateChanged();
            session.NotifyGenerationCompleted();
        }
    }

    private Agent ReloadAgent(Agent agent)
    {
        try
        {
            return Agent.Load(agent.Guid, _kvData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GenerationSessionService] Failed to reload agent {AgentGuid}, using in-memory copy: {Message}",
                agent.Guid, ex.Message);
            return agent;
        }
    }

    // ---- 回调处理 ----

    private void HandleStreamDelta(GenerationSession session, Chat chat, ResponseEx accumulated)
    {
        var regenerating = chat.Messages.FirstOrDefault(
            m => m.GenerationState == MessageGenerationState.Regenerating);
        Message genMsg;

        if (regenerating != null)
        {
            genMsg = regenerating;
            if (genMsg.CurrentVersion != null)
            {
                genMsg.CurrentVersion.Content = "";
                genMsg.CurrentVersion.thinking = null;
                genMsg.CurrentVersion.toolCalls = null;
            }
            genMsg.GenerationState = MessageGenerationState.Generating;
            chat.SaveMessage(_messageStore, genMsg);
        }
        else
        {
            var existingGen = chat.Messages.FirstOrDefault(
                m => m.GenerationState == MessageGenerationState.Generating);
            if (existingGen != null)
            {
                genMsg = existingGen;
            }
            else
            {
                genMsg = new Message
                {
                    message = new ChatMessage { Content = "", thinking = string.Empty },
                    timestamp = DateTime.Now,
                    sender = Sender.AI,
                    GenerationState = MessageGenerationState.Generating
                };
                chat.AddMessage(genMsg);
                chat.SaveMessage(_messageStore, genMsg);
            }
        }

        session.GeneratingMessageId = genMsg.Id;

        // 更新消息内容（流式增量）
        if (!string.IsNullOrEmpty(accumulated.Body.Content))
        {
            if (genMsg.CurrentVersion != null)
                genMsg.CurrentVersion.Content = accumulated.Body.Content;
            else
                genMsg.message = new ChatMessage { Content = accumulated.Body.Content };
        }

        if (accumulated.Body.thinking != null)
        {
            if (genMsg.CurrentVersion != null)
                genMsg.CurrentVersion.thinking = accumulated.Body.thinking;
            else if (genMsg.message != null)
                genMsg.message.thinking = accumulated.Body.thinking;
        }

        if (accumulated.Body.toolCalls != null)
        {
            if (genMsg.CurrentVersion != null)
                genMsg.CurrentVersion.toolCalls = accumulated.Body.toolCalls;
            else if (genMsg.message != null)
                genMsg.message.toolCalls = accumulated.Body.toolCalls;
        }

        session.NotifyScrollRequested();
        session.NotifyStateChanged();
    }

    private void HandleAssistantComplete(GenerationSession session, Chat chat, ResponseEx accumulated,
        string? originalContent, Message? continuationMessage)
    {
        var genMsg = chat.Messages.FirstOrDefault(m => m.Id == session.GeneratingMessageId);
        if (genMsg == null)
        {
            // 后处理节点可能删掉了本轮消息（例如判定该轮无效并替换为自定义消息）。
            // 消息已经不在 Chat 里，这里不再回写，也不试图重建。
            if (session.GeneratingMessageId != Guid.Empty)
                _logger.LogInformation(
                    "[GenerationSessionService] Round message {MessageId} was removed by the post-generation pipeline; skipping write-back",
                    session.GeneratingMessageId);
            session.GeneratingMessageId = Guid.Empty;
            return;
        }

        if (continuationMessage != null)
        {
            // 续写模式：保留原始前缀
            if (genMsg.CurrentVersion != null)
                genMsg.CurrentVersion.Content = (originalContent ?? "") + accumulated.Body.Content;
        }
        else
        {
            if (genMsg.CurrentVersion != null)
                genMsg.CurrentVersion.Content = accumulated.Body.Content;
        }

        genMsg.GenerationState = MessageGenerationState.Completed;
        if (continuationMessage != null)
            genMsg.IsContinuation = false;
        chat.SaveMessage(_messageStore, genMsg);

        session.NotifyAgentMessageCompleted(genMsg);
        session.GeneratingMessageId = Guid.Empty;
    }

    /// <summary>
    /// 后处理节点本轮追加的消息。节点已负责写库，这里只把它们同步给插件面板与界面。
    /// </summary>
    private void HandleCommittedMessages(GenerationSession session, Chat chat, IReadOnlyList<Message> appended)
    {
        foreach (var msg in appended)
        {
            if (msg.sender == Sender.ToolResult)
                session.NotifyToolResultAdded(msg);
            else if (msg.sender == Sender.AI)
                session.NotifyAgentMessageCompleted(msg);
        }

        session.NotifyScrollRequested();
        session.NotifyStateChanged();
    }

    private void GetDirty(Chat chat, Agent agent)
    {
        chat.LastModifyTime = DateTime.Now;
        chat.Save(_kvData);

        // 提供器生成的 Chat 不在 Agent.ChatGuids 中（记录在事件的 GeneratedChatGuids），
        // MoveChatToTop 对它是无操作，无需因此整写 Agent
        if (chat.ProviderSource != null)
            return;

        // 只更新 ChatGuids 顺序，避免整对象 Save 覆盖其它连接（如 Agent 编辑器）的并发编辑。
        try
        {
            Agent.Update(agent.Guid, _kvData, a => a.MoveChatToTop(chat.Guid));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[GenerationSessionService] Failed to persist chat order for agent {AgentGuid}: {Message}",
                agent.Guid, ex.Message);
        }
    }
}
