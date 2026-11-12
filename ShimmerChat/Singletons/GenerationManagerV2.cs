using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatBuiltin.Fragments;
using ShimmerChatBuiltin.Misc.Node.PreGeneration;
using ShimmerChatBuiltin.NodeBasic.PreGeneration;
using ShimmerChatBuiltin.ToolManage;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// ShimmerChat 2.0 生成管道管理器。
    /// 执行前生成树 → 构建 Prompt → 调用 API → 轮次循环。
    /// 是否继续下一轮、要不要执行工具调用，全部由后处理管线决定；
    /// 这里的宿主只负责时序、提交与安全网。
    /// </summary>
    public class GenerationManagerV2 : IGenerationManagerV2
    {
        private readonly IKVDataService _kvData;
        private readonly IToolRegistry _toolRegistry;
        private readonly IPreGenerationNodeSerializer _serializer;
        private readonly IPostGenerationManagerService _postManager;
        private readonly ILocService _locService;
        private readonly IDebugOutputService _debugOutput;
        private readonly IImageAttachmentService _imageAttachments;
        private readonly IMessageStoreService _messageStore;
        private readonly IToolInteractionService _toolInteractions;
        private readonly IVectorStoreService _vectorStore;
        private readonly ToolCallLoop _loop = new();
        private readonly ILogger<GenerationManagerV2> _logger;
        private readonly IServiceProvider _services;

        public GenerationManagerV2(IKVDataService kvData, IToolRegistry toolRegistry,
            IPreGenerationNodeSerializer serializer, IPostGenerationManagerService postManager,
            ILocService locService, IDebugOutputService debugOutput,
            IImageAttachmentService imageAttachments, IMessageStoreService messageStore,
            IToolInteractionService toolInteractions, IVectorStoreService vectorStore,
            ILogger<GenerationManagerV2> logger, IServiceProvider services)
        {
            _kvData = kvData;
            _toolRegistry = toolRegistry;
            _serializer = serializer;
            _postManager = postManager;
            _locService = locService;
            _debugOutput = debugOutput;
            _imageAttachments = imageAttachments;
            _messageStore = messageStore;
            _toolInteractions = toolInteractions;
            _vectorStore = vectorStore;
            _logger = logger;
            _services = services;
            EnsureDefaultPreset();
        }

        private void EnsureDefaultPreset()
        {
            var json = _kvData.Read("GenerationManager", "generation_presets");
            var presets = string.IsNullOrEmpty(json)
                ? new List<PreGenerationPreset>()
                : (JsonConvert.DeserializeObject<List<PreGenerationPreset>>(json) ?? new List<PreGenerationPreset>());

            presets.RemoveAll(p => p.Id == "__default__");

            presets.Add(new PreGenerationPreset
            {
                Id = "__default__",
                Name = "Default",
                RootNodeJson = _serializer.Serialize(new SequenceNode
                {
                    Name = "Default",
                    Nodes = new List<IPreGenerationNode>
                    {
                        new FragmentNode
                        {
                            Name = "System Prompt",
                            Content = "You are a helpful AI assistant.",
                            From = PromptBuilder.From.system
                        },
                        new AppendChatMessagesNode { Name = "Append Chat Messages" },
                        new APISelectNode { Name = "Select API" },
                        new ToolPresetNode { Name = "Load Tools", PresetId = "" }
                    }
                })
            });

            _kvData.Write("GenerationManager", "generation_presets",
                JsonConvert.SerializeObject(presets, Formatting.Indented));
        }

        /// <summary>
        /// 流式生成 AI 响应
        /// </summary>
        public async Task GenerateStreamAsync(
            Agent agent,
            Chat chat,
            Func<ResponseEx, Task> onStreamDelta,
            Func<ResponseEx, Task> onAssistantComplete,
            Func<IReadOnlyList<Message>, Task>? onMessagesCommitted = null,
            Func<Task>? onPostGenerationStarted = null,
            PipelineTreeOverrides? overrides = null,
            CancellationToken cancellationToken = default)
        {
            var env = await BuildEnvironment(agent, chat, cancellationToken, overrides);

            try
            {
                var host = new MainLoopHost(this, agent, chat, env,
                    onStreamDelta, onAssistantComplete, onMessagesCommitted,
                    onPostGenerationStarted, overrides, cancellationToken);

                var apiSetting = env.Transient.API
                    ?? throw new InvalidOperationException("No API configured.");

                // 前生成管线未产出任何消息时明确报错（常见于事件覆盖树缺少
                // 历史/片段注入节点），避免 LLM 报晦涩的 'messages' array cannot be empty
                if (env.Transient.Fragments.Count == 0)
                    throw new InvalidOperationException(
                        "Pre-generation pipeline produced no messages (Fragments is empty). " +
                        "Add a chat-history or fragment node to the pre-generation tree.");

                if (!apiSetting.SupportsToolCalling && env.Transient.Tools.Count > 0)
                    _logger.LogWarning("[GenerationManagerV2] Warning: API does not support tool calling, but {Count} tool(s) are registered.", env.Transient.Tools.Count);

                await _loop.RunAsync(
                    apiSetting.ChatClient,
                    env.Transient.Tools.Select(t => t.GetDefinition()).ToList(),
                    host,
                    streaming: apiSetting.SupportsStreaming,
                    ct: cancellationToken);
            }
            finally
            {
                // 释放本次生成注册的会话级资源（本地进程句柄 / HTTP 会话等）。
                await DisposeGenerationResourcesAsync(env.Persistent);
            }
        }

        /// <summary>
        /// 释放前生成节点在 <see cref="PersistentEnv"/> 上注册的会话级资源。
        /// 释放失败不能中断已经完成的生成，但必须记录（不静默吞掉）。
        /// </summary>
        private async Task DisposeGenerationResourcesAsync(PersistentEnv persistent)
        {
            if (persistent.Resources.Count == 0) return;

            try
            {
                await persistent.Resources.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GenerationManagerV2] Failed to dispose generation-scoped resources.");
                _debugOutput.Write("GenerationManagerV2", "ResourceCleanup",
                    $"[GenerationManagerV2] Generation-scoped resource cleanup failed: {ex}");
            }
        }

        /// <summary>
        /// 非流式生成 AI 响应
        /// </summary>
        public async Task GenerateAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Func<IReadOnlyList<Message>, Task>? onMessagesCommitted = null)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await GenerateStreamAsync(
                agent, chat,
                onStreamDelta: rsp => { onResponse(rsp); return Task.CompletedTask; },
                onAssistantComplete: _ => Task.CompletedTask,
                onMessagesCommitted: onMessagesCommitted,
                cancellationToken: cts.Token);
        }

        /// <summary>
        /// 构建生成环境：执行前生成树 + 加载历史消息。
        /// overrides 非空时用其前生成树 JSON 替代 Agent 的设置（生成提供器场景）。
        /// </summary>
        public async Task<PreGenerationEnv> BuildEnvironment(
            Agent agent, Chat chat, CancellationToken ct,
            PipelineTreeOverrides? overrides = null)
        {
            var persistent = new PersistentEnv
            {
                KVData = _kvData,
                ToolRegistry = _toolRegistry,
                Serializer = _serializer,
                LocService = _locService,
                DebugOutput = _debugOutput,
                PostGenerationManager = _postManager,
                ImageAttachments = _imageAttachments,
                Interactions = _toolInteractions,
                VectorStore = _vectorStore,
                Chat = chat,
                Agent = agent,
                MessageStore = _messageStore,
                Services = _services
            };

            IPreGenerationNode rootNode;
            var preTreeJson = overrides?.PreGenerationTreeJson ?? agent.PreGenerationTreeJson;
            if (!string.IsNullOrEmpty(preTreeJson))
            {
                rootNode = _serializer.Deserialize(preTreeJson)
                    ?? CreateFallbackRoot(agent);
            }
            else
            {
                rootNode = CreateFallbackRoot(agent);
            }

            var env = new PreGenerationEnv(persistent);

            // 续写检测：最后一条 AI 消息带 IsContinuation 标记
            var lastMsg = chat.Messages.LastOrDefault();
            if (lastMsg?.IsContinuation == true)
                env.Transient.SharedState["IsContinuation"] = true;

            var context = new PreNodeExecutionContext(env, ct);
            var result = await rootNode.ExecuteAsync(context);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"Generation tree execution failed. Node: '{result.NodeName}' ({result.NodeId}). {result.Message}");

            return env;
        }

        /// <summary>
        /// 后生成处理：执行后生成管线。节点直接操作对话与存储。
        /// </summary>
        public async Task<PostPipelineResult> PostProcessAsync(
            Agent agent, ChatMessage responseMessage,
            IReadOnlyList<ContextSegment> preFragments, PersistentEnv persistentEnv,
            PostPipelineContext? roundContext = null, CancellationToken ct = default,
            PipelineTreeOverrides? overrides = null)
        {
            return await _postManager.ExecuteAsync(
                agent, responseMessage, preFragments, persistentEnv, roundContext, ct, overrides);
        }

        /// <summary>
        /// 轮次循环宿主：负责时序、提交与安全网，不做业务判断。
        /// </summary>
        private class MainLoopHost : IToolCallLoopHost
        {
            private readonly GenerationManagerV2 _manager;
            private readonly Agent _agent;
            private readonly Chat _chat;
            private PreGenerationEnv _env;
            private readonly Func<ResponseEx, Task> _onStreamDelta;
            private readonly Func<ResponseEx, Task> _onAssistantComplete;
            private readonly Func<IReadOnlyList<Message>, Task>? _onMessagesCommitted;
            private readonly Func<Task>? _onPostGenerationStarted;
            private readonly PipelineTreeOverrides? _overrides;
            private readonly CancellationToken _ct;

            /// <summary>跨轮复用的节点共享状态，前后管线共用同一份字典。</summary>
            private readonly Dictionary<string, object> _loopState = new();

            private int _roundIndex;

            public MainLoopHost(
                GenerationManagerV2 manager,
                Agent agent,
                Chat chat,
                PreGenerationEnv env,
                Func<ResponseEx, Task> onStreamDelta,
                Func<ResponseEx, Task> onAssistantComplete,
                Func<IReadOnlyList<Message>, Task>? onMessagesCommitted,
                Func<Task>? onPostGenerationStarted,
                PipelineTreeOverrides? overrides,
                CancellationToken ct)
            {
                _manager = manager;
                _agent = agent;
                _chat = chat;
                _env = env;
                _onStreamDelta = onStreamDelta;
                _onAssistantComplete = onAssistantComplete;
                _onMessagesCommitted = onMessagesCommitted;
                _onPostGenerationStarted = onPostGenerationStarted;
                _overrides = overrides;
                _ct = ct;

                _env.Transient.SharedState = _loopState;
            }

            public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
            {
                var pb = new PromptBuilder
                {
                    Messages = _env.Transient.Fragments.Select(s => (s.Message, s.From)).ToArray()
                };

                if (toolDefinitions.Count > 0)
                {
                    pb.AvailableTools = toolDefinitions.ToList();
                    pb.AvailableToolsFormatter = ToolPromptParser.Parse;
                }

                return pb;
            }

            public async Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
            {
                await _onStreamDelta(accumulated);
            }

            public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
            {
                if (_onPostGenerationStarted != null)
                    await _onPostGenerationStarted();

                // 快照消息集合：后处理节点会直接增删消息，靠比较前后 Id 集合找出新增项
                var beforeIds = new HashSet<Guid>(_chat.Messages.Select(m => m.Id));
                var current = FindCurrentMessage();

                var result = await RunPostPipelineAsync(fullResponse, current, ct);

                fullResponse.Body = result.ResponseMessage;
                await _onAssistantComplete(fullResponse);

                await CommitAppendedAsync(beforeIds);
                VerifyRound();

                var toolCalls = result.ResponseMessage.toolCalls;
                if (toolCalls is { Count: > 0 } && !result.ToolCallsHandled)
                {
                    // 没有节点接管工具调用：继续循环只会重复拿到同样的调用，必须停下并留下证据
                    _manager._logger.LogWarning(
                        "[GenerationManagerV2] Round {Round}: response has {Count} tool call(s) but no post-generation node handled them. " +
                        "Stopping instead of looping. Add a 'Tool Calls' node to the post-generation tree.",
                        _roundIndex, toolCalls.Count);
                    _manager._debugOutput.Write("GenerationManagerV2", "PostGeneration",
                        $"Round {_roundIndex}: {toolCalls.Count} unhandled tool call(s). " +
                        $"Names: {string.Join(", ", toolCalls.Select(t => t.name))}. Loop stopped.");
                    return false;
                }

                if (result.LoopDirective != PostLoopDirective.Continue)
                    return false;

                _roundIndex++;

                // 重建前生成环境：AppendChatMessagesNode 等节点实时读取 Chat.Messages，
                // 因此上一轮节点追加的消息会自动进入下一轮上下文
                _env = await _manager.BuildEnvironment(_agent, _chat, ct, _overrides);
                _env.Transient.SharedState = _loopState;
                return true;
            }

            private async Task<PostPipelineResult> RunPostPipelineAsync(
                ResponseEx fullResponse, Message? current, CancellationToken ct)
            {
                try
                {
                    return await _manager.PostProcessAsync(
                        _agent, fullResponse.Body,
                        _env.Transient.Fragments, _env.Persistent,
                        new PostPipelineContext
                        {
                            Tools = _env.Transient.Tools,
                            CurrentMessage = current,
                            RoundIndex = _roundIndex,
                            SharedState = _loopState
                        },
                        ct, _overrides);
                }
                catch (Exception ex)
                {
                    _manager._logger.LogError(ex,
                        "[GenerationManagerV2] Post-Generation threw, stopping the round with the raw response.");
                    return new PostPipelineResult
                    {
                        Success = false,
                        Code = NodeErrorCodes.CommonError,
                        Message = ex.Message,
                        ResponseMessage = fullResponse.Body,
                        LoopDirective = PostLoopDirective.Stop
                    };
                }
            }

            /// <summary>把节点追加的消息交给宿主同步到插件面板与滚动。</summary>
            private async Task CommitAppendedAsync(HashSet<Guid> beforeIds)
            {
                if (_onMessagesCommitted == null)
                    return;

                var appended = _chat.Messages
                    .Where(m => !beforeIds.Contains(m.Id))
                    .ToList();

                if (appended.Count == 0)
                    return;

                await _onMessagesCommitted(appended);
            }

            /// <summary>
            /// 回合末的护栏：节点可以绕过 Chat API 直接改集合，也可能忘记写库。
            /// 这里只负责把问题暴露出来，不自动修复（自动重排会掩盖调用方的错误）。
            /// </summary>
            private void VerifyRound()
            {
                var orderError = _chat.ValidateOrder();
                if (orderError != null)
                {
                    _manager._logger.LogWarning(
                        "[GenerationManagerV2] Round {Round}: message order is inconsistent — {Error}. " +
                        "Call Chat.NormalizeOrder() and re-persist if this came from a node.",
                        _roundIndex, orderError);
                    _manager._debugOutput.Write("GenerationManagerV2", "PostGeneration",
                        $"Round {_roundIndex}: order check failed — {orderError}");
                }

                if (_env.Persistent.MessageStore is not { } store)
                    return;

                var storedCount = store.GetMessageCount(_chat.Guid);
                if (storedCount != _chat.Messages.Count)
                {
                    _manager._logger.LogWarning(
                        "[GenerationManagerV2] Round {Round}: chat has {InMemory} message(s) but store has {Stored}. " +
                        "A post-generation node probably modified Chat.Messages without calling SaveMessage.",
                        _roundIndex, _chat.Messages.Count, storedCount);
                    _manager._debugOutput.Write("GenerationManagerV2", "PostGeneration",
                        $"Round {_roundIndex}: message count mismatch — memory {_chat.Messages.Count}, store {storedCount}.");
                }
            }

            private Message? FindCurrentMessage()
                => _chat.Messages.LastOrDefault(m => m.IsGenerating);
        }

        private static IPreGenerationNode CreateFallbackRoot(Agent agent)
        {
            var root = new SequenceNode { Name = agent.Name };

            root.Nodes.Add(new CallNode { PresetId = "__default__" });

            return root;
        }
    }
}
