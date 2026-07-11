using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.sub_agent", Icon = "🤖", Color = "#e06090", CategoryKeys = ["category.flow", "category.sub_agent"])]
    public class SubAgentNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent";

        [NodeProperty("prop.sub_agent.config_name", HintKey = "prop.sub_agent.config_name.hint")]
        public string ConfigName { get; set; } = "";

        [NodeProperty("prop.sub_agent.task", HintKey = "prop.sub_agent.task.hint")]
        public string Task { get; set; } = "";

        [NodeProperty("prop.sub_agent.output_mode", HintKey = "prop.sub_agent.output_mode.hint")]
        public string OutputMode { get; set; } = "";

        [NodeProperty("prop.sub_agent.max_iterations", HintKey = "prop.sub_agent.max_iterations.hint")]
        public int MaxIterations { get; set; } = 50;

        private static readonly GenerationTreeExecutor _treeExecutor = new();

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(ConfigName))
                return NodeResult.SuccessResult();

            var kvData = context.Env.Persistent.KVData;
            var config = LoadConfig(kvData, ConfigName);
            if (config == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    $"SubAgent: Config '{ConfigName}' not found.", nodeId: Id, nodeName: Name);

            var rootNode = ResolveTree(config, kvData, context.Env.Persistent.Serializer);
            if (rootNode == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    $"SubAgent: No modifier tree configured for '{ConfigName}'. Assign a preset or create a private tree.",
                    nodeId: Id, nodeName: Name);

            // 1. 创建隔离的 GenerationEnv，执行 SubAgent 修饰器树
            var persistent = new PersistentEnv
            {
                KVData = kvData,
                ChatGuid = context.Env.Persistent.ChatGuid,
                AgentGuid = context.Env.Persistent.AgentGuid,
                ToolRegistry = context.Env.Persistent.ToolRegistry,
                Serializer = context.Env.Persistent.Serializer
            };

            // 1. 创建隔离 env，将对话历史放入 SharedState（由树中的 AppendChatMessages 节点负责注入）
            var subEnv = new GenerationEnv(persistent);
            var chat = context.Env.Persistent.GetChat();
            subEnv.Transient.SharedState["ChatMessages"] = chat.Messages.ToList();

            // 2. 执行 SubAgent 修饰器树（树产物追加在历史之后）
            try
            {
                var ctx = new NodeExecutionContext(subEnv, context.CancellationToken);
                var result = await rootNode.ExecuteAsync(ctx);
                if (!result.Success)
                    return NodeResult.Failure(NodeErrorCodes.ServiceError,
                        $"SubAgent: Tree execution failed: {result.Message}", nodeId: Id, nodeName: Name);
            }
            catch (Exception ex)
            {
                return NodeResult.Failure(NodeErrorCodes.ServiceError,
                    $"SubAgent: Tree execution failed: {ex.Message}", nodeId: Id, nodeName: Name);
            }

            // 3. API：父级优先，树未设置则继承父级
            subEnv.Transient.API ??= context.Env.Transient.API;
            if (subEnv.Transient.API == null)
                return NodeResult.Failure(NodeErrorCodes.ApiUnavailable,
                    "SubAgent: No API configured.", nodeId: Id, nodeName: Name);

            // 3. 若节点显式设置了 Task，追加为 user 消息
            if (!string.IsNullOrWhiteSpace(Task))
            {
                subEnv.Transient.Fragments.Add(new ContextSegment
                {
                    Message = new ChatMessage { Content = Task },
                    From = PromptBuilder.From.user
                });
            }

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
                await loop.RunAsync(subEnv.Transient.API, toolDefs, host,
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

        private static IGenerationNode? ResolveTree(SubAgent.SubAgentConfig config, IKVDataService kvData, IGenerationNodeSerializer serializer)
        {
            if (!config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierTreeJson))
                return serializer.Deserialize(config.ModifierTreeJson);

            if (config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierPresetId))
            {
                var json = kvData.Read("GenerationManager", "generation_presets");
                var presets = JsonConvert.DeserializeObject<List<GenerationPreset>>(json ?? "[]") ?? [];
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
    }
}
