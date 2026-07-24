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
    private readonly ILogger<GenerationSessionService> _logger;

    public GenerationSessionService(
        IGenerationManagerV2 generationManager,
        IMessageStoreService messageStore,
        IKVDataService kvData,
        ILocService loc,
        ILogger<GenerationSessionService> logger)
    {
        _generationManager = generationManager;
        _messageStore = messageStore;
        _kvData = kvData;
        _loc = loc;
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
    /// </summary>
    public void CancelPreviousGeneration(string chatGuid)
    {
        if (_sessions.TryGetValue(chatGuid, out var session) && session.IsActive)
        {
            session.Cancel();
            if (session.RunningTask != null)
            {
                try { session.RunningTask.GetAwaiter().GetResult(); } catch { }
            }

            session.IsActive = false;
            session.Phase = null;
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
    }

    /// <summary>
    /// 停止生成（用户点击停止按钮）。
    /// </summary>
    public async Task StopGeneration(string chatGuid)
    {
        if (!_sessions.TryGetValue(chatGuid, out var session) || !session.IsActive)
            return;

        session.Cancel();
        if (session.RunningTask != null)
        {
            try { await session.RunningTask; } catch { }
        }

        session.IsActive = false;
        session.Phase = null;
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

    // ---- 启动生成 ----

    /// <summary>
    /// 启动普通生成。会话持有 Chat/Agent 引用，消息操作直接作用在 Chat.Messages 上。
    /// </summary>
    public void StartGeneration(GenerationSession session, Agent agent, Chat chat,
        bool throwExceptionInsteadOfPopup = false)
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
            RunGenerationLoop(session, agent, chat, continuationMessage: null,
                throwExceptionInsteadOfPopup, ct));
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
                throwExceptionInsteadOfPopup, ct));
    }

    // ---- 生成主循环 ----

    private async Task RunGenerationLoop(
        GenerationSession session,
        Agent agent,
        Chat chat,
        Message? continuationMessage,
        bool throwExceptionInsteadOfPopup,
        CancellationToken ct)
    {
        string? originalContent = continuationMessage?.CurrentVersion?.Content;

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
                onToolCall: _ =>
                {
                    session.NotifyStateChanged();
                },
                onToolResult: tuple =>
                {
                    HandleToolResult(session, chat, tuple);
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

            if (throwExceptionInsteadOfPopup)
                throw;

            session.NotifyError(
                $"{ex.Message} \n {ex.InnerException?.Message ?? ""}",
                _loc.Format("chat.gen_error", ex.Source ?? "[NULL]"));
        }
        finally
        {
            session.IsActive = false;
            session.DisposeCts();
            GetDirty(chat, agent);
            session.NotifyStateChanged();
            session.NotifyGenerationCompleted();
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
        if (genMsg != null)
        {
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
        }

        session.GeneratingMessageId = Guid.Empty;
    }

    private void HandleToolResult(GenerationSession session, Chat chat,
        (string name, string resp, string id) tuple)
    {
        var toolMsg = new Message
        {
            message = new ChatMessage
            {
                Content = tuple.resp,
                id = tuple.id,
                thinking = null
            },
            timestamp = DateTime.Now,
            sender = Sender.ToolResult
        };
        chat.AddMessage(toolMsg);
        chat.SaveMessage(_messageStore, toolMsg);

        session.NotifyToolResultAdded(toolMsg);
        session.NotifyScrollRequested();
        session.NotifyStateChanged();
    }

    private void GetDirty(Chat chat, Agent agent)
    {
        chat.LastModifyTime = DateTime.Now;
        chat.Save(_kvData);
        agent.MoveChatToTop(chat.Guid);
        agent.Save(_kvData);
    }
}
