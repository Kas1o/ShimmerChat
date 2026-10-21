using System.Collections.ObjectModel;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent.Sessions;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IToolV2 版本的 SubAgent 工具。
    /// 内部维护 SubAgent 注册列表，GetDefinition 动态返回可用枚举。
    /// <para>
    /// 每次调用都会落成一条持久会话：结果以 JSON 信封返回（含 <c>session_id</c>），
    /// 调用方传回 <c>session_id</c> 即可继续同一会话。会话存在 KVData 空间 <c>SubAgent</c>，
    /// 不写入 Chats / 消息存储。
    /// </para>
    /// </summary>
    public class SubAgentToolV2 : IToolV2
    {
        /// <summary>工具返回信封里的错误码。面向 LLM，使用英文短标识。</summary>
        public static class ErrorCodes
        {
            public const string InvalidArguments = "INVALID_ARGUMENTS";
            public const string SubagentRequired = "SUBAGENT_REQUIRED";
            public const string SubagentNotRegistered = "SUBAGENT_NOT_REGISTERED";
            public const string TreeNotFound = "TREE_NOT_FOUND";
            public const string InvalidSessionId = "INVALID_SESSION_ID";
            public const string SessionNotFound = "SESSION_NOT_FOUND";
            public const string SessionOwnerMismatch = "SESSION_OWNER_MISMATCH";
            public const string SessionStoreError = "SESSION_STORE_ERROR";
            public const string NoApi = "NO_API";
            public const string TreeFailed = "TREE_FAILED";
            public const string LoopFailed = "LOOP_FAILED";
        }

        private readonly IKVDataService _kvData;
        private readonly IToolRegistry _toolRegistry;
        private readonly Guid _chatGuid;
        private readonly Guid _agentGuid;
        private readonly IPreGenerationNodeSerializer _serializer;
        private readonly ILocService _locService;
        private readonly IDebugOutputService _debugOutput;
        private readonly IPostGenerationManagerService? _postGenerationManager;

        private readonly List<SubAgentEntry> _entries = new();

        /// <summary>父级 Chat Guid（<c>SharedChatGuid = true</c> 时使用）。</summary>
        public Guid ParentChatGuid => _chatGuid;

        /// <summary>父级 Agent Guid（<c>SharedAgentGuid = true</c> 时使用）。</summary>
        public Guid ParentAgentGuid => _agentGuid;

        public SubAgentToolV2(IKVDataService kvData,
            IToolRegistry toolRegistry, Guid chatGuid, Guid agentGuid,
            IPreGenerationNodeSerializer serializer, ILocService locService,
            IDebugOutputService debugOutput,
            IPostGenerationManagerService? postGenerationManager = null)
        {
            _kvData = kvData;
            _toolRegistry = toolRegistry;
            _chatGuid = chatGuid;
            _agentGuid = agentGuid;
            _serializer = serializer;
            _locService = locService;
            _debugOutput = debugOutput;
            _postGenerationManager = postGenerationManager;
        }

        /// <summary>注册一个 SubAgent 配置（使用继承父级身份的默认选项）。</summary>
        public void AddSubAgent(SubAgentConfig config) => AddSubAgent(config, SubAgentToolOptions.InheritParent);

        /// <summary>
        /// 注册一个 SubAgent 配置。选项按配置项携带：同一轮生成里多个 SubAgentToolNode
        /// 共享本工具实例，开关不能挂在工具级别。
        /// </summary>
        public void AddSubAgent(SubAgentConfig config, SubAgentToolOptions options)
        {
            var guidStr = config.Guid.ToString();
            if (!_entries.Any(e => e.ConfigGuid == guidStr))
            {
                _entries.Add(new SubAgentEntry
                {
                    ConfigGuid = guidStr,
                    ConfigName = config.Name,
                    Config = config,
                    Options = options
                });
            }
        }

        public Tool GetDefinition()
        {
            var names = _entries.Select(e => e.ConfigName).ToList();
            return new Tool
            {
                name = "subagent_call",
                description = names.Count > 0
                    ? "Invoke a registered sub-agent. The result is a JSON envelope containing "
                      + "'session_id' and 'output'; to continue the same conversation (keeping its history), "
                      + "pass that 'session_id' back. Omit 'session_id' to start a new session. "
                      + $"Available sub-agents: {string.Join(", ", names)}"
                    : "Invoke a registered sub-agent. (None registered yet.)",
                parameters =
                [
                    (new ToolParameter
                    {
                        name = "subagent",
                        type = ParameterType.String,
                        description = $"Name of the sub-agent to invoke. Available: {string.Join(", ", names)}",
                        @enum = names
                    }, true),
                    (new ToolParameter
                    {
                        name = "task",
                        type = ParameterType.String,
                        description = "The task description for the sub-agent."
                    }, true),
                    (new ToolParameter
                    {
                        name = "session_id",
                        type = ParameterType.String,
                        description = "Optional. 'session_id' returned by a previous call; pass it to continue that session. "
                                      + "Omit to start a new session."
                    }, false),
                    (new ToolParameter
                    {
                        name = "title",
                        type = ParameterType.String,
                        description = "Optional. Title for a new session; ignored when continuing an existing session."
                    }, false)
                ]
            };
        }

        public async Task<string> ExecuteAsync(string input)
        {
            SubAgentCallArgs? args;
            try
            {
                args = string.IsNullOrWhiteSpace(input)
                    ? null
                    : JsonConvert.DeserializeObject<SubAgentCallArgs>(input);
            }
            catch (Exception ex)
            {
                return Serialize(ErrorEnvelope(null, null, ErrorCodes.InvalidArguments,
                    $"Cannot parse tool arguments as JSON: {ex.Message}"));
            }

            if (args == null || string.IsNullOrWhiteSpace(args.subagent))
                return Serialize(ErrorEnvelope(null, null, ErrorCodes.SubagentRequired, "subagent is required."));

            var entry = _entries.FirstOrDefault(e => e.ConfigName == args.subagent);
            if (entry == null)
                return Serialize(ErrorEnvelope(null, args.subagent, ErrorCodes.SubagentNotRegistered,
                    $"SubAgent '{args.subagent}' is not registered. " +
                    $"Available: {string.Join(", ", _entries.Select(e => e.ConfigName))}"));

            var config = entry.Config;

            // 对齐 SubAgentNode：树解析失败时返回错误而非静默降级
            var rootNode = ResolveTree(config, _kvData);
            if (rootNode == null)
                return Serialize(ErrorEnvelope(null, config.Name, ErrorCodes.TreeNotFound,
                    $"Tree for '{config.Name}' not found. Check config or shared preset."));

            var task = string.IsNullOrWhiteSpace(args.task) ? "Execute the configured task." : args.task!;
            var sessionIdArg = !string.IsNullOrWhiteSpace(args.session_id) ? args.session_id : args.chat_id;

            SubAgentSessionRunScope scope;
            if (!string.IsNullOrWhiteSpace(sessionIdArg))
            {
                if (!Guid.TryParse(sessionIdArg, out var sessionId))
                    return Serialize(ErrorEnvelope(null, config.Name, ErrorCodes.InvalidSessionId,
                        $"session_id '{sessionIdArg}' is not a valid GUID."));

                var opened = SubAgentSessionStore.Open(_kvData, _debugOutput, sessionId);
                if (opened.Status == SubAgentSessionLoadStatus.NotFound)
                    return Serialize(ErrorEnvelope(sessionId, config.Name, ErrorCodes.SessionNotFound,
                        $"Session '{sessionId}' was not found. Omit session_id to start a new session."));
                if (opened.Scope == null)
                    return Serialize(ErrorEnvelope(sessionId, config.Name, ErrorCodes.SessionStoreError,
                        $"Session '{sessionId}' could not be loaded: {opened.Error}"));

                scope = opened.Scope;
                if (scope.Session.ConfigGuid != config.Guid)
                {
                    var owner = $"'{scope.Session.ConfigName}' ({scope.Session.ConfigGuid})";
                    scope.Dispose();
                    return Serialize(ErrorEnvelope(sessionId, config.Name, ErrorCodes.SessionOwnerMismatch,
                        $"Session '{sessionId}' belongs to sub-agent config {owner}, not " +
                        $"'{config.Name}' ({config.Guid}). Omit session_id to start a new session."));
                }

                // 续聊：本次 task 作为新的用户消息追加到会话历史
                scope.Append(SubAgentSessionMessages.FromPrompt(
                    new ChatMessage { Content = task }, PromptBuilder.From.user));
            }
            else
            {
                var started = SubAgentSessionStore.StartNew(
                    _kvData, _debugOutput, config,
                    SubAgentSession.BuildTitle(args.title, task),
                    SubAgentSessionMessages.FromPrompt(new ChatMessage { Content = task }, PromptBuilder.From.user),
                    _chatGuid, _agentGuid);

                if (started.Scope == null)
                    return Serialize(ErrorEnvelope(null, config.Name, ErrorCodes.SessionStoreError,
                        $"Failed to create a session for '{config.Name}': {started.Error}"));

                scope = started.Scope;
            }

            using (scope)
            {
                return await RunAsync(scope, entry, rootNode);
            }
        }

        private async Task<string> RunAsync(SubAgentSessionRunScope scope, SubAgentEntry entry, IPreGenerationNode rootNode)
        {
            var config = entry.Config;
            var session = scope.Session;

            // 种子消息 = 会话历史（新建会话时只有本次 task；续聊时是旧历史 + 本次 task）
            var seedMessages = session.Messages.ToList();

            // SharedChatGuid / SharedAgentGuid 决定子代理树看到的作用域身份
            var chatGuid = entry.Options.SharedChatGuid ? _chatGuid : session.Id;
            var agentGuid = entry.Options.SharedAgentGuid ? _agentGuid : config.Guid;

            var persistent = new PersistentEnv
            {
                KVData = _kvData,
                ToolRegistry = _toolRegistry,
                Serializer = _serializer,
                LocService = _locService,
                DebugOutput = _debugOutput,
                PostGenerationManager = _postGenerationManager,
                Chat = new Chat { Name = session.Title, Guid = chatGuid },
                Agent = CreateVirtualAgent(agentGuid)
            };
            persistent.Chat.Messages = new ObservableCollection<Message>(seedMessages);

            var subEnv = new PreGenerationEnv(persistent);

            // 执行修饰器树（与 SubAgentNode / PostSubAgentNode 一致）
            try
            {
                var ctx = new PreNodeExecutionContext(subEnv, CancellationToken.None);
                var result = await rootNode.ExecuteAsync(ctx);
                if (!result.Success)
                    return Fail(scope, config, ErrorCodes.TreeFailed,
                        $"[SubAgent Tree Error: {result.Message}]", result.Message);
            }
            catch (Exception ex)
            {
                return Fail(scope, config, ErrorCodes.TreeFailed, $"[SubAgent Tree Error: {ex.Message}]", ex.Message);
            }

            if (subEnv.Transient.API == null)
                return Fail(scope, config, ErrorCodes.NoApi,
                    "Error: No API configured for SubAgent. Add an APISelectNode to the modifier tree.",
                    "No API configured for SubAgent.");

            var api = subEnv.Transient.API.ChatClient;
            var tools = subEnv.Transient.Tools;
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();
            var toolExecutor = new ToolV2Executor(tools);

            var promptCtx = new SubAgentPromptContext(
                subEnv.Transient.Fragments.Select(s => (s.Message, s.From)));

            // 后处理器：始终走后生成管线（对齐主流程）
            Func<ChatMessage, Task<ChatMessage>>? postProcessor = null;
            if (_postGenerationManager != null)
            {
                var postAgent = Agent.Create("__sub_post__", "");
                postAgent.PostGenerationTreeJson = config.PostGenerationTreeJson ?? "";
                var fragments = subEnv.Transient.Fragments;
                var mgr = _postGenerationManager;
                var cfgName = config.Name;
                postProcessor = async msg =>
                {
                    try
                    {
                        return await mgr.ExecuteAsync(postAgent, msg, fragments, persistent, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _debugOutput.Write("SubAgentToolV2", "PostGeneration",
                            $"[SubAgentToolV2] Post-Generation failed for '{cfgName}': {ex}");
                        return msg;
                    }
                };
            }

            // 环境重建函数：每次工具调用后重执行修饰器树。
            // 内存路径保持「种子消息 + conversation」，不依赖落盘成功。
            Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? rebuildFragments = async (conversation) =>
            {
                var fullChatMessages = new List<Message>(seedMessages);
                fullChatMessages.AddRange(conversation.Select(a => SubAgentSessionMessages.FromPrompt(a)));

                persistent.Chat.Messages = new ObservableCollection<Message>(fullChatMessages);
                var newEnv = new PreGenerationEnv(persistent);
                var newCtx = new PreNodeExecutionContext(newEnv, CancellationToken.None);
                await rootNode.ExecuteAsync(newCtx);
                return newEnv.Transient.Fragments.ToList();
            };

            // 增量落盘：每条 assistant / tool_result 立刻写回会话
            Action<(ChatMessage Message, PromptBuilder.From From)> onMessageAppended =
                appended => scope.Append(SubAgentSessionMessages.FromPrompt(appended));

            var host = new SubAgentLoopHost(promptCtx, toolExecutor, postProcessor, rebuildFragments, onMessageAppended);
            var loop = new ToolCallLoop();

            try
            {
                await loop.RunAsync(api, toolDefs, host,
                    maxRounds: Int32.MaxValue,
                    continueOnToolError: true,
                    ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                return Fail(scope, config, ErrorCodes.LoopFailed, $"[SubAgent Error: {ex.Message}]", ex.Message);
            }

            var outputText = SubAgentFormatter.Format(config.OutputMode, host.CurrentContext);
            scope.Complete(outputText);

            return Serialize(SuccessEnvelope(scope, config, outputText));
        }

        #region 结果信封

        private string Fail(SubAgentSessionRunScope scope, SubAgentConfig config,
            string code, string displayMessage, string? detail)
        {
            scope.Fail(code, detail ?? displayMessage);
            return Serialize(ErrorEnvelope(scope.Session.Id, config.Name, code, displayMessage, scope));
        }

        private static ToolEnvelope SuccessEnvelope(
            SubAgentSessionRunScope scope, SubAgentConfig config, string outputText)
        {
            var envelope = new ToolEnvelope
            {
                Ok = true,
                SessionId = scope.Session.Id.ToString(),
                Subagent = config.Name,
                Status = StatusText(scope.Session.Status),
                MessageCount = scope.Session.Messages.Count,
                Output = outputText
            };
            ApplyPersistState(envelope, scope);
            return envelope;
        }

        private static ToolEnvelope ErrorEnvelope(
            Guid? sessionId, string? subagentName, string code, string error, SubAgentSessionRunScope? scope = null)
        {
            var envelope = new ToolEnvelope
            {
                Ok = false,
                SessionId = sessionId?.ToString(),
                Subagent = subagentName,
                Status = scope != null ? StatusText(scope.Session.Status) : null,
                MessageCount = scope?.Session.Messages.Count,
                Code = code,
                Error = error
            };
            if (scope != null)
                ApplyPersistState(envelope, scope);
            return envelope;
        }

        private static void ApplyPersistState(ToolEnvelope envelope, SubAgentSessionRunScope scope)
        {
            if (scope.PersistError == null)
                return;

            // 落盘失败不中断调用，但必须让调用方看到结果没有持久化
            envelope.Persisted = false;
            envelope.PersistError = scope.PersistError;
        }

        private static string StatusText(SubAgentSessionStatus status) => status switch
        {
            SubAgentSessionStatus.Running => "running",
            SubAgentSessionStatus.Completed => "completed",
            SubAgentSessionStatus.Failed => "failed",
            _ => status.ToString().ToLowerInvariant()
        };

        private static string Serialize(ToolEnvelope envelope)
            => JsonConvert.SerializeObject(envelope, Formatting.None);

        private sealed class ToolEnvelope
        {
            [JsonProperty("ok")] public bool Ok { get; set; }

            [JsonProperty("session_id", NullValueHandling = NullValueHandling.Ignore)]
            public string? SessionId { get; set; }

            [JsonProperty("subagent", NullValueHandling = NullValueHandling.Ignore)]
            public string? Subagent { get; set; }

            [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
            public string? Status { get; set; }

            [JsonProperty("message_count", NullValueHandling = NullValueHandling.Ignore)]
            public int? MessageCount { get; set; }

            [JsonProperty("output", NullValueHandling = NullValueHandling.Ignore)]
            public string? Output { get; set; }

            [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
            public string? Code { get; set; }

            [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
            public string? Error { get; set; }

            [JsonProperty("persisted", NullValueHandling = NullValueHandling.Ignore)]
            public bool? Persisted { get; set; }

            [JsonProperty("persist_error", NullValueHandling = NullValueHandling.Ignore)]
            public string? PersistError { get; set; }
        }

        #endregion

        private IPreGenerationNode? ResolveTree(SubAgentConfig config, IKVDataService kvData)
        {
            if (!config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierTreeJson))
                return _serializer.Deserialize(config.ModifierTreeJson);

            if (config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierPresetId))
            {
                var json = kvData.Read("GenerationManager", "generation_presets");
                var presets = JsonConvert.DeserializeObject<List<PreGenerationPreset>>(json ?? "[]") ?? [];
                var preset = presets.FirstOrDefault(p => p.Id == config.ModifierPresetId);
                if (preset != null)
                    return _serializer.Deserialize(preset.RootNodeJson);
            }

            return null;
        }

        private class SubAgentEntry
        {
            public string ConfigGuid { get; set; } = "";
            public string ConfigName { get; set; } = "";
            public SubAgentConfig Config { get; set; } = null!;
            public SubAgentToolOptions Options { get; set; } = SubAgentToolOptions.InheritParent;
        }

        private class SubAgentCallArgs
        {
            [JsonProperty("subagent")] public string? subagent { get; set; }
            [JsonProperty("task")] public string? task { get; set; }

            /// <summary>续聊目标会话。模型偶尔会照抄用户的说法写成 chat_id，这里一并接受。</summary>
            [JsonProperty("session_id")] public string? session_id { get; set; }

            [JsonProperty("chat_id")] public string? chat_id { get; set; }

            [JsonProperty("title")] public string? title { get; set; }
        }

        private static Agent CreateVirtualAgent(Guid guid)
        {
            var agent = Agent.Create("sub", "");
            agent.Guid = guid;
            return agent;
        }
    }
}
