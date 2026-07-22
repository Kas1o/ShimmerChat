using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IToolV2 版本的 SubAgent 工具。
    /// 内部维护 SubAgent 注册列表，GetDefinition 动态返回可用枚举。
    /// </summary>
    public class SubAgentToolV2 : IToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly IToolRegistry _toolRegistry;
        private readonly Chat _chat;
        private readonly Agent _agent;
        private readonly IPreGenerationNodeSerializer _serializer;
        private readonly ILocService _locService;
        private readonly IDebugOutputService _debugOutput;
        private readonly IPostGenerationManagerService? _postGenerationManager;

        private readonly List<SubAgentEntry> _entries = new();

        public SubAgentToolV2(IKVDataService kvData,
            IToolRegistry toolRegistry, Chat chat, Agent agent,
            IPreGenerationNodeSerializer serializer, ILocService locService,
            IDebugOutputService debugOutput,
            IPostGenerationManagerService? postGenerationManager = null)
        {
            _kvData = kvData;
            _toolRegistry = toolRegistry;
            _chat = chat;
            _agent = agent;
            _serializer = serializer;
            _locService = locService;
            _debugOutput = debugOutput;
            _postGenerationManager = postGenerationManager;
        }

        /// <summary>注册一个 SubAgent 配置。</summary>
        public void AddSubAgent(SubAgentConfig config)
        {
            var guidStr = config.Guid.ToString();
            if (!_entries.Any(e => e.ConfigGuid == guidStr))
                _entries.Add(new SubAgentEntry { ConfigGuid = guidStr, ConfigName = config.Name, Config = config });
        }

        public Tool GetDefinition()
        {
            var names = _entries.Select(e => e.ConfigName).ToList();
            return new Tool
            {
                name = "subagent_call",
                description = names.Count > 0
                    ? $"Invoke a registered sub-agent. Available: {string.Join(", ", names)}"
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
                    }, true)
                ]
            };
        }

        public async Task<string> ExecuteAsync(string input)
        {
            var args = JsonConvert.DeserializeObject<SubAgentCallArgs>(input);
            if (args == null || string.IsNullOrWhiteSpace(args.subagent))
                return "Error: subagent is required.";

            var entry = _entries.FirstOrDefault(e => e.ConfigName == args.subagent);
            if (entry == null)
                return $"Error: SubAgent '{args.subagent}' is not registered. Available: {string.Join(", ", _entries.Select(e => e.ConfigName))}";

            var config = entry.Config;

            // 对齐 SubAgentNode：树解析失败时返回错误而非静默降级
            var rootNode = ResolveTree(config, _kvData);
            if (rootNode == null)
                return $"[SubAgent Error] Tree for '{config.Name}' not found. Check config or shared preset.";

            var persistent = new PersistentEnv
            {
                KVData = _kvData,
                ToolRegistry = _toolRegistry,
                Serializer = _serializer,
                LocService = _locService,
                DebugOutput = _debugOutput,
                PostGenerationManager = _postGenerationManager,  // 对齐主流程：设置后处理器
                Chat = _chat,
                Agent = _agent
            };

            var subEnv = new PreGenerationEnv(persistent);

            // 构造 seed 对话（仅用户任务消息）
            var chatMessages = new List<Message>
            {
                new Message
                {
                    message = new ChatMessage { Content = args.task ?? "Execute the configured task." },
                    sender = ShimmerChatLib.Sender.User,
                    timestamp = DateTime.Now
                }
            };
            subEnv.Transient.SharedState["ChatMessages"] = chatMessages;

            // 执行修饰器树（与 SubAgentNode / PostSubAgentNode 一致）
            try
            {
                var ctx = new PreNodeExecutionContext(subEnv, CancellationToken.None);
                var result = await rootNode.ExecuteAsync(ctx);
                if (!result.Success)
                    return $"[SubAgent Tree Error: {result.Message}]";
            }
            catch (Exception ex) { return $"[SubAgent Tree Error: {ex.Message}]"; }

            if (subEnv.Transient.API == null)
                return "Error: No API configured for SubAgent. Add an APISelectNode to the modifier tree.";

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
                            $"[SubAgentToolV2] Post-Generation failed for '{cfgName}': {ex.Message}");
                        return msg;
                    }
                };
            }

            // 环境重建函数：每次工具调用后重执行修饰器树。
            // 接收对话增量（assistant + tool_result），与种子消息合并后注入 SharedState。
            Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? rebuildFragments = async (conversation) =>
            {
                var fullChatMessages = new List<Message>();
                fullChatMessages.AddRange(chatMessages);  // 种子消息
                fullChatMessages.AddRange(conversation.Select(a => new Message
                {
                    message = a.Item1,
                    sender = a.Item2 switch
                    {
                        PromptBuilder.From.user => Sender.User,
                        PromptBuilder.From.system => Sender.System,
                        PromptBuilder.From.assistant => Sender.AI,
                        PromptBuilder.From.tool_result => Sender.ToolResult,
                        _ => Sender.System
                    },
                    timestamp = DateTime.Now
                }).ToList());

                var newEnv = new PreGenerationEnv(persistent);
                newEnv.Transient.SharedState["ChatMessages"] = fullChatMessages;
                var newCtx = new PreNodeExecutionContext(newEnv, CancellationToken.None);
                await rootNode.ExecuteAsync(newCtx);
                return newEnv.Transient.Fragments.ToList();
            };

            var host = new SubAgentLoopHost(promptCtx, toolExecutor, postProcessor, rebuildFragments);
            var loop = new ToolCallLoop();

            try
            {
                await loop.RunAsync(api, toolDefs, host,
                    maxRounds: 50,
                    continueOnToolError: true,
                    ct: CancellationToken.None);
            }
            catch (Exception ex) { return $"[SubAgent Error: {ex.Message}]"; }

            return SubAgentFormatter.Format(config.OutputMode, host.CurrentContext);
        }

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
        }

        private class SubAgentCallArgs
        {
            [JsonProperty("subagent")] public string? subagent { get; set; }
            [JsonProperty("task")] public string? task { get; set; }
        }
    }
}
