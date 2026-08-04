using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 生成提供器宿主服务。
    ///
    /// 职责：
    /// 1. 启动时扫描所有 Agent 的生成事件，实例化并启动对应提供器；
    /// 2. 提供 <see cref="IProviderTriggerService"/>：创建 Chat、写入触发消息、
    ///    以事件的管线树覆盖执行一次完整生成（复用 GenerationSessionService）；
    /// 3. 维护事件运行状态（LastTriggerTime / LastRunStatus），错误落盘 DebugOutput。
    ///
    /// 注册为 Singleton + IHostedService。
    /// </summary>
    public class GenerationProviderHostService : IHostedService, IProviderTriggerService
    {
        /// <summary>每个事件最多保留的生成 Chat 记录数</summary>
        private const int MaxGeneratedChats = 100;

        private readonly IGenerationProviderRegistry _registry;
        private readonly IGenerationEventStore _eventStore;
        private readonly IKVDataService _kvData;
        private readonly IMessageStoreService _messageStore;
        private readonly GenerationSessionService _sessionService;
        private readonly IDebugOutputService _debugOutput;
        private readonly ILocService _loc;
        private readonly ILogger<GenerationProviderHostService> _logger;

        /// <summary>运行中的提供器实例，键为 "{agentGuid}:{eventId}"</summary>
        private readonly ConcurrentDictionary<string, IGenerationProvider> _running = new();

        public GenerationProviderHostService(
            IGenerationProviderRegistry registry,
            IGenerationEventStore eventStore,
            IKVDataService kvData,
            IMessageStoreService messageStore,
            GenerationSessionService sessionService,
            IDebugOutputService debugOutput,
            ILocService loc,
            ILogger<GenerationProviderHostService> logger)
        {
            _registry = registry;
            _eventStore = eventStore;
            _kvData = kvData;
            _messageStore = messageStore;
            _sessionService = sessionService;
            _debugOutput = debugOutput;
            _loc = loc;
            _logger = logger;
        }

        private static string MakeKey(Guid agentGuid, string eventId) => $"{agentGuid}:{eventId}";

        // ─── IHostedService ───

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 后台加载，避免阻塞应用启动（提供器 StartAsync 可能做网络监听等操作）
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var guid in Agent.GetAllAgentGuids(_kvData))
                    {
                        await ReloadAgentAsync(guid);
                    }
                    _logger.LogInformation(
                        "[GenerationProviderHost] Startup complete. Running providers: {Count}", _running.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GenerationProviderHost] Startup error: {Message}", ex.Message);
                }
            });
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var key in _running.Keys.ToList())
            {
                if (_running.TryRemove(key, out var provider))
                {
                    try { await provider.StopAsync(); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[GenerationProviderHost] Stop provider {Key} error: {Message}",
                            key, ex.Message);
                    }
                }
            }
        }

        // ─── 事件生命周期管理（供 UI 调用） ───

        /// <summary>指定事件是否有提供器实例正在运行</summary>
        public bool IsEventRunning(Guid agentGuid, string eventId)
            => _running.ContainsKey(MakeKey(agentGuid, eventId));

        /// <summary>
        /// 重新加载单个事件：停止旧实例，若事件存在且启用则重新启动。
        /// 事件配置修改后由 UI 调用。
        /// </summary>
        public async Task ReloadEventAsync(Guid agentGuid, string eventId)
        {
            await StopEventAsync(agentGuid, eventId);
            var evt = _eventStore.GetEvent(agentGuid, eventId);
            if (evt != null && evt.Enabled)
                await StartEventAsync(agentGuid, evt);
        }

        /// <summary>
        /// 重新加载指定 Agent 的全部事件（停止旧的、启动启用的）。
        /// </summary>
        public async Task ReloadAgentAsync(Guid agentGuid)
        {
            var prefix = agentGuid.ToString() + ":";
            foreach (var key in _running.Keys.Where(k => k.StartsWith(prefix)).ToList())
            {
                await StopEventAsync(agentGuid, key[prefix.Length..]);
            }

            foreach (var evt in _eventStore.GetEvents(agentGuid))
            {
                if (evt.Enabled)
                    await StartEventAsync(agentGuid, evt);
            }
        }

        /// <summary>停止指定事件的提供器实例（事件删除/禁用时由 UI 调用）</summary>
        public async Task StopEventAsync(Guid agentGuid, string eventId)
        {
            if (_running.TryRemove(MakeKey(agentGuid, eventId), out var provider))
            {
                try
                {
                    await provider.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[GenerationProviderHost] Stop provider error (Agent {AgentGuid}, Event {EventId}): {Message}",
                        agentGuid, eventId, ex.Message);
                }
            }
        }

        private async Task StartEventAsync(Guid agentGuid, GenerationEvent evt)
        {
            var typeInfo = _registry.GetProviderType(evt.ProviderTypeName);
            if (typeInfo == null)
            {
                evt.LastRunStatus = "PROVIDER_MISSING";
                _eventStore.UpdateEvent(agentGuid, evt);
                _logger.LogWarning(
                    "[GenerationProviderHost] Provider type '{TypeName}' not found (plugin missing?). Agent {AgentGuid}, Event {EventId}",
                    evt.ProviderTypeName, agentGuid, evt.Id);
                return;
            }

            var instance = _registry.CreateInstance(typeInfo);
            if (instance == null)
                return;

            if (!string.IsNullOrEmpty(evt.ConfigJson))
            {
                try
                {
                    ProviderConfigHelper.Populate(instance, evt.ConfigJson);
                }
                catch (Exception ex)
                {
                    evt.LastRunStatus = $"CONFIG_ERROR: {ex.Message}";
                    _eventStore.UpdateEvent(agentGuid, evt);
                    _logger.LogError(ex,
                        "[GenerationProviderHost] Config deserialization failed (Agent {AgentGuid}, Event {EventId}): {Message}",
                        agentGuid, evt.Id, ex.Message);
                    return;
                }
            }

            var runtime = new ProviderRuntimeContext
            {
                AgentGuid = agentGuid,
                Event = evt,
                TriggerService = this,
                DebugOutput = _debugOutput,
                KVData = _kvData,
                LocService = _loc
            };

            try
            {
                await instance.StartAsync(runtime);
                _running[MakeKey(agentGuid, evt.Id)] = instance;
            }
            catch (Exception ex)
            {
                evt.LastRunStatus = $"START_ERROR: {ex.Message}";
                _eventStore.UpdateEvent(agentGuid, evt);
                _debugOutput.Write("GenerationProvider", "error",
                    $"[{agentGuid}/{evt.Id}] Provider start failed: {ex.Message}");
                _logger.LogError(ex,
                    "[GenerationProviderHost] Provider start failed (Agent {AgentGuid}, Event {EventId}): {Message}",
                    agentGuid, evt.Id, ex.Message);
            }
        }

        // ─── IProviderTriggerService ───

        /// <summary>
        /// 触发一次生成：创建 Chat（带来源标记与 Render 树盖印）→ 写入触发消息 →
        /// 以提供器声明的树覆盖执行生成 → 回写事件状态。
        /// triggerText / overrides 未显式传入时，从运行中（或临时实例化并
        /// 填充配置的）提供器实例拉取。
        /// </summary>
        public async Task<Guid> TriggerAsync(Guid agentGuid, string eventId,
            string? triggerText = null, PipelineTreeOverrides? overrides = null,
            CancellationToken ct = default)
        {
            var evt = _eventStore.GetEvent(agentGuid, eventId)
                ?? throw new InvalidOperationException(
                    $"Generation event '{eventId}' not found for agent '{agentGuid}'.");

            // 触发文本与管线覆盖的来源实例：优先运行中的提供器，
            // 否则临时实例化并填充配置（事件禁用时手动触发也能使用正确配置）
            IGenerationProvider sourceProvider;
            if (_running.TryGetValue(MakeKey(agentGuid, eventId), out var runningProvider))
            {
                sourceProvider = runningProvider;
            }
            else
            {
                var typeInfo = _registry.GetProviderType(evt.ProviderTypeName)
                    ?? throw new InvalidOperationException(
                        $"Provider type '{evt.ProviderTypeName}' not found for event '{eventId}' (plugin missing?).");
                sourceProvider = _registry.CreateInstance(typeInfo)
                    ?? throw new InvalidOperationException(
                        $"Failed to create provider instance of '{evt.ProviderTypeName}'.");
                ProviderConfigHelper.Populate(sourceProvider, evt.ConfigJson);
            }

            triggerText ??= sourceProvider.GetManualTriggerText();
            overrides ??= sourceProvider.GetPipelineOverrides();

            var agent = Agent.Load(agentGuid, _kvData);

            var chat = new Chat
            {
                Guid = Guid.NewGuid(),
                Name = $"{evt.Name} {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                CreateTime = DateTime.Now,
                LastModifyTime = DateTime.Now,
                ProviderSource = evt.ProviderTypeName,
                SourceEventId = evt.Id,
                // Render 覆盖随 Chat 盖印，渲染阶段直接读取，不受事件配置后续修改影响
                SourceRenderModifierTreeJson = overrides?.RenderModifierTreeJson
            };

            if (!string.IsNullOrWhiteSpace(triggerText))
            {
                var triggerMsg = new Message
                {
                    sender = Sender.System,
                    timestamp = DateTime.Now,
                    message = new ChatMessage { Content = triggerText }
                };
                chat.AddMessage(triggerMsg);
                chat.SaveMessage(_messageStore, triggerMsg);
            }

            chat.Save(_kvData);

            // 回写事件状态与生成的 Chat 记录
            evt.LastTriggerTime = DateTime.Now;
            if (!evt.GeneratedChatGuids.Contains(chat.Guid))
                evt.GeneratedChatGuids.Insert(0, chat.Guid);
            if (evt.GeneratedChatGuids.Count > MaxGeneratedChats)
                evt.GeneratedChatGuids.RemoveRange(MaxGeneratedChats,
                    evt.GeneratedChatGuids.Count - MaxGeneratedChats);
            _eventStore.UpdateEvent(agentGuid, evt);

            var chatGuidStr = chat.Guid.ToString();
            var session = _sessionService.GetOrCreateSession(chatGuidStr, agent.Guid.ToString());
            _sessionService.StartGeneration(session, agent, chat,
                throwExceptionInsteadOfPopup: true, overrides: overrides);

            var running = session.RunningTask;
            if (running != null)
            {
                try
                {
                    await running;
                    evt.LastRunStatus = "OK";
                }
                catch (OperationCanceledException)
                {
                    evt.LastRunStatus = "CANCELLED";
                }
                catch (Exception ex)
                {
                    evt.LastRunStatus = $"ERROR: {ex.Message}";
                    _debugOutput.Write("GenerationProvider", "error",
                        $"[{agentGuid}/{eventId}] Generation failed: {ex}");
                    _logger.LogError(ex,
                        "[GenerationProviderHost] Trigger generation failed (Agent {AgentGuid}, Event {EventId})",
                        agentGuid, eventId);
                }
                _eventStore.UpdateEvent(agentGuid, evt);
            }

            _sessionService.CleanupSession(chatGuidStr);
            return chat.Guid;
        }
    }
}
