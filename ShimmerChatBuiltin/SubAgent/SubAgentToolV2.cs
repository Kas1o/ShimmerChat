using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Generation.Nodes;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IToolV2 版本的 SubAgent 工具。
    /// 内部维护 SubAgent 注册列表，GetDefinition 动态返回可用枚举。
    /// </summary>
    public class SubAgentToolV2 : IToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly IChatCompletionClient? _api;
        private readonly IToolRegistry _toolRegistry;
        private readonly Guid _chatGuid;
        private readonly Guid _agentGuid;
        private readonly IGenerationNodeSerializer _serializer;
        private readonly ILocService _locService;

        private readonly List<SubAgentEntry> _entries = new();

        public SubAgentToolV2(IKVDataService kvData, IChatCompletionClient? api,
            IToolRegistry toolRegistry, Guid chatGuid, Guid agentGuid,
            IGenerationNodeSerializer serializer, ILocService locService)
        {
            _kvData = kvData;
            _api = api;
            _toolRegistry = toolRegistry;
            _chatGuid = chatGuid;
            _agentGuid = agentGuid;
            _serializer = serializer;
            _locService = locService;
        }

        /// <summary>注册一个 SubAgent 配置。</summary>
        public void AddSubAgent(string configName, SubAgentConfig config)
        {
            if (!_entries.Any(e => e.ConfigName == configName))
                _entries.Add(new SubAgentEntry { ConfigName = configName, Config = config });
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

        private static readonly GenerationTreeExecutor _treeExecutor = new();

        public async Task<string> ExecuteAsync(string input)
        {
            var args = JsonConvert.DeserializeObject<SubAgentCallArgs>(input);
            if (args == null || string.IsNullOrWhiteSpace(args.subagent))
                return "Error: subagent is required.";

            var entry = _entries.FirstOrDefault(e => e.ConfigName == args.subagent);
            if (entry == null)
                return $"Error: SubAgent '{args.subagent}' is not registered. Available: {string.Join(", ", _entries.Select(e => e.ConfigName))}";

            if (_api == null)
                return "Error: No API available for SubAgent.";

            var config = entry.Config;

            var rootNode = ResolveTree(config, _kvData);
            var persistent = new PersistentEnv
            {
                KVData = _kvData,
                ChatGuid = _chatGuid,
                AgentGuid = _agentGuid,
                ToolRegistry = _toolRegistry,
                Serializer = _serializer,
                LocService = _locService
            };

            GenerationEnv subEnv;
            try { subEnv = await _treeExecutor.ExecuteAsync(rootNode, persistent); }
            catch (Exception ex) { return $"[SubAgent Tree Error: {ex.Message}]"; }

            var api = subEnv.Transient.API?.ChatClient ?? _api;
            var tools = subEnv.Transient.Tools;
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();
            var toolExecutor = new ToolV2Executor(tools);

            subEnv.Transient.Fragments.Add(new ContextSegment
            {
                Message = new ChatMessage { Content = args.task ?? "Execute the configured task." },
                From = PromptBuilder.From.user
            });

            var promptCtx = new SubAgentPromptContext(
                subEnv.Transient.Fragments.Select(s => (s.Message, s.From)));

            var host = new SubAgentLoopHost(promptCtx, toolExecutor);
            var loop = new ToolCallLoop();

            try { await loop.RunAsync(api, toolDefs, host); }
            catch (Exception ex) { return $"[SubAgent Error: {ex.Message}]"; }

            return SubAgentFormatter.Format(config.OutputMode, promptCtx);
        }

        private IGenerationNode ResolveTree(SubAgentConfig config, IKVDataService kvData)
        {
            if (!config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierTreeJson))
                return _serializer.Deserialize(config.ModifierTreeJson)
                    ?? new SequenceNode { Name = config.Name };

            if (config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierPresetId))
            {
                var json = kvData.Read("GenerationManager", "generation_presets");
                var presets = JsonConvert.DeserializeObject<List<GenerationPreset>>(json ?? "[]") ?? [];
                var preset = presets.FirstOrDefault(p => p.Id == config.ModifierPresetId);
                if (preset != null)
                    return _serializer.Deserialize(preset.RootNodeJson)
                        ?? new SequenceNode { Name = config.Name };
            }

            return new SequenceNode { Name = config.Name };
        }

        private class SubAgentEntry
        {
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
