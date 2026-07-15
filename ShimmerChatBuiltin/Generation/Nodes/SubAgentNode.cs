using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.sub_agent", Icon = "🤖", Color = "var(--node-subagent)", CategoryKeys = ["category.flow", "category.sub_agent"])]
    public class SubAgentNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent";

        [NodeProperty("prop.sub_agent.config_name", HintKey = "prop.sub_agent.config_name.hint")]
        public string ConfigName { get; set; } = "";

        [NodeProperty("prop.sub_agent.output_mode", HintKey = "prop.sub_agent.output_mode.hint")]
        public string OutputMode { get; set; } = "";

        [NodeProperty("prop.sub_agent.max_iterations", HintKey = "prop.sub_agent.max_iterations.hint")]
        public int MaxIterations { get; set; } = 50;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(ConfigName))
                return NodeResult.SuccessResult();

            var kvData = context.Env.Persistent.KVData;
            var config = LoadConfig(kvData, ConfigName);
            if (config == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_config_not_found", ConfigName), nodeId: Id, nodeName: Name);

            var rootNode = ResolveTree(config, kvData, context.Env.Persistent.Serializer);
            if (rootNode == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_no_tree", ConfigName),
                    nodeId: Id, nodeName: Name);

            // 1. 创建隔离的 PreGenerationEnv，将父级 Fragments 转为临时对话写入 SharedState
            var persistent = new PersistentEnv
            {
                KVData = kvData,
                ChatGuid = context.Env.Persistent.ChatGuid,
                AgentGuid = context.Env.Persistent.AgentGuid,
                ToolRegistry = context.Env.Persistent.ToolRegistry,
                Serializer = context.Env.Persistent.Serializer,
                LocService = context.Env.Persistent.LocService,
                DebugOutput = context.Env.Persistent.DebugOutput
            };

            var subEnv = new PreGenerationEnv(persistent);

            var chatMessages = new List<Message>();
            foreach (var seg in context.Env.Transient.Fragments)
            {
                chatMessages.Add(new Message
                {
                    message = seg.Message,
                    sender = FragmentFromToSender(seg.From),
                    timestamp = DateTime.Now
                });
            }
            subEnv.Transient.SharedState["ChatMessages"] = chatMessages;

            // 2. 执行 SubAgent 修饰器树（树产物追加在历史之后）
            try
            {
                var ctx = new PreNodeExecutionContext(subEnv, context.CancellationToken);
                var result = await rootNode.ExecuteAsync(ctx);
                if (!result.Success)
                    return NodeResult.Failure(NodeErrorCodes.ServiceError,
                        loc.Format("node_err.subagent_tree_failed", result.Message), nodeId: Id, nodeName: Name);
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(NodeErrorCodes.ServiceError,
                    loc.Format("node_err.subagent_tree_failed", ex.Message), nodeId: Id, nodeName: Name);
            }

            // 3. API：父级优先，树未设置则继承父级
            subEnv.Transient.API ??= context.Env.Transient.API;
            if (subEnv.Transient.API == null)
                return NodeResult.Failure(NodeErrorCodes.ApiUnavailable,
                    loc["node_err.subagent_no_api"], nodeId: Id, nodeName: Name);

            // 4. Tool Call 循环（隔离 env 内独立运行）
            var tools = subEnv.Transient.Tools;
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();
            var toolExecutor = new SubAgent.ToolV2Executor(tools);
            var promptCtx = new SubAgent.SubAgentPromptContext(
                subEnv.Transient.Fragments.Select(s => (s.Message, s.From)));

            var host = new SubAgent.SubAgentLoopHost(promptCtx, toolExecutor);
            var loop = new ToolCallLoop();

            try
            {
                var api = subEnv.Transient.API?.ChatClient
                    ?? throw new InvalidOperationException("No API configured for SubAgent.");
                await loop.RunAsync(api, toolDefs, host,
                    maxRounds: MaxIterations,
                    continueOnToolError: true,
                    ct: context.CancellationToken);
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(NodeErrorCodes.ServiceError,
                    $"[SubAgent Error: {ex.Message}]", nodeId: Id, nodeName: Name);
            }

            // 5. 输出格式化，注入父级上下文
            var mode = !string.IsNullOrWhiteSpace(OutputMode) ? OutputMode : config.OutputMode;
            if (mode == "None")
                return NodeResult.SuccessResult();

            var outputText = SubAgent.SubAgentFormatter.Format(mode, promptCtx);
            if (!string.IsNullOrEmpty(outputText))
            {
                context.Env.Transient.Fragments.Add(new ContextSegment
                {
                    SourceType = typeof(SubAgentNode),
                    Message = new ChatMessage { Content = outputText },
                    From = PromptBuilder.From.assistant,
                    Metadata = new Dictionary<string, object>
                    {
                        ["subAgentName"] = ConfigName,
                        ["outputMode"] = mode
                    }
                });
            }

            return NodeResult.SuccessResult();
        }

        private static IPreGenerationNode? ResolveTree(SubAgent.SubAgentConfig config, IKVDataService kvData, IPreGenerationNodeSerializer serializer)
        {
            if (!config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierTreeJson))
                return serializer.Deserialize(config.ModifierTreeJson);

            if (config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierPresetId))
            {
                var json = kvData.Read("GenerationManager", "generation_presets");
                var presets = JsonConvert.DeserializeObject<List<PreGenerationPreset>>(json ?? "[]") ?? [];
                var preset = presets.FirstOrDefault(p => p.Id == config.ModifierPresetId);
                if (preset != null)
                    return serializer.Deserialize(preset.RootNodeJson);
            }

            return null;
        }

        private static SubAgent.SubAgentConfig? LoadConfig(IKVDataService kvData, string name)
        {
            var json = kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgent.SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }

        private static string FragmentFromToSender(PromptBuilder.From from) => from switch
        {
            PromptBuilder.From.user => Sender.User,
            PromptBuilder.From.system => Sender.System,
            PromptBuilder.From.assistant => Sender.AI,
            PromptBuilder.From.tool_result => Sender.ToolResult,
            _ => Sender.System
        };
    }
}
