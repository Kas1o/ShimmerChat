using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    [NodeInfo("node.sub_agent", Icon = "🤖", Color = "var(--node-subagent)", CategoryKeys = ["category.flow", "category.sub_agent"])]
    [NodeEditor(typeof(SubAgentNodeEditor))]
    public class SubAgentNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent";

        public string ConfigGuid { get; set; } = "";

        [NodeProperty("prop.sub_agent.output_mode", HintKey = "prop.sub_agent.output_mode.hint")]
        public string OutputMode { get; set; } = "";

        [NodeProperty("prop.sub_agent.max_iterations", HintKey = "prop.sub_agent.max_iterations.hint")]
        public int MaxIterations { get; set; } = 50;

        [NodeProperty("prop.sub_agent.shared_guid", HintKey = "prop.sub_agent.shared_guid.hint")]
        public bool SharedGuid { get; set; } = false;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(ConfigGuid))
                return NodeResult.SuccessResult();

            var kvData = context.Env.Persistent.KVData;
            var config = LoadConfig(kvData, ConfigGuid);
            if (config == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_config_not_found", ConfigGuid), nodeId: Id, nodeName: Name);

            var rootNode = ResolveTree(config, kvData, context.Env.Persistent.Serializer);
            if (rootNode == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_no_tree", config.Name),
                    nodeId: Id, nodeName: Name);

            // 1. 创建隔离的 PreGenerationEnv，将父级 Fragments 转为临时对话写入 SharedState
            var persistent = new PersistentEnv
            {
                KVData = kvData,
                ToolRegistry = context.Env.Persistent.ToolRegistry,
                Serializer = context.Env.Persistent.Serializer,
                LocService = context.Env.Persistent.LocService,
                DebugOutput = context.Env.Persistent.DebugOutput,
                PostGenerationManager = context.Env.Persistent.PostGenerationManager,
                Chat = context.Env.Persistent.Chat,
                Agent = SharedGuid ? context.Env.Persistent.Agent : Agent.Load(config.Guid, kvData)
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

            // 3. API：由修饰器树中的 APISelectNode 设置
            if (subEnv.Transient.API == null)
                return NodeResult.Failure(NodeErrorCodes.ApiUnavailable,
                    loc["node_err.subagent_no_api"], nodeId: Id, nodeName: Name);

            // 4. Tool Call 循环（隔离 env 内独立运行），每轮 assistant 响应经由 Post-Generation 管线
            var tools = subEnv.Transient.Tools;
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();
            var toolExecutor = new ToolV2Executor(tools);
            var promptCtx = new SubAgentPromptContext(
                subEnv.Transient.Fragments.Select(s => (s.Message, s.From)));

            // 后处理器：始终走后生成管线（对齐主流程，每轮 assistant 响应都触发）
            Func<ChatMessage, Task<ChatMessage>>? postProcessor = null;
            if (persistent.PostGenerationManager != null)
            {
                var postAgent = Agent.Create("__sub_post__", "");
                postAgent.PostGenerationTreeJson = config.PostGenerationTreeJson ?? "";
                var fragments = subEnv.Transient.Fragments;
                var mgr = persistent.PostGenerationManager;
                var ct = context.CancellationToken;
                var cfgName = config.Name;
                var debug = context.Env.Persistent.DebugOutput;
                postProcessor = async msg =>
                {
                    try
                    {
                        return await mgr.ExecuteAsync(postAgent, msg, fragments, persistent, ct);
                    }
                    catch (Exception ex)
                    {
                        debug.Write("SubAgentNode", "PostGeneration",
                            $"[SubAgentNode] Post-Generation failed for '{cfgName}': {ex.Message}");
                        return msg;
                    }
                };
            }

            // 环境重建函数：每次工具调用后重执行修饰器树。
            // 接收对话增量（assistant + tool_result），与种子消息合并后注入 SharedState，
            // 让 AppendChatMessagesNode 等节点能感知完整上下文。
            Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? rebuildFragments = async (conversation) =>
            {
                var fullChatMessages = new List<Message>();
                fullChatMessages.AddRange(chatMessages);  // 种子消息（来自父级 Fragments）
                fullChatMessages.AddRange(conversation.Select(a => new Message
                {
                    message = a.Item1,
                    sender = FragmentFromToSender(a.Item2),
                    timestamp = DateTime.Now
                }));

                var newEnv = new PreGenerationEnv(persistent);
                newEnv.Transient.SharedState["ChatMessages"] = fullChatMessages;
                var newCtx = new PreNodeExecutionContext(newEnv, context.CancellationToken);
                await rootNode.ExecuteAsync(newCtx);
                return newEnv.Transient.Fragments.ToList();
            };

            var host = new SubAgentLoopHost(promptCtx, toolExecutor, postProcessor, rebuildFragments);
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

            var outputText = SubAgent.SubAgentFormatter.Format(mode, host.CurrentContext);
            if (!string.IsNullOrEmpty(outputText))
            {
                context.Env.Transient.Fragments.Add(new ContextSegment
                {
                    SourceType = typeof(SubAgentNode),
                    Message = new ChatMessage { Content = outputText },
                    From = PromptBuilder.From.assistant,
                    Metadata = new Dictionary<string, object>
                    {
                        ["subAgentName"] = config.Name,
                        ["outputMode"] = mode
                    }
                });
            }

            return NodeResult.SuccessResult();
        }

        private static IPreGenerationNode? ResolveTree(SubAgentConfig config, IKVDataService kvData, IPreGenerationNodeSerializer serializer)
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

        private static SubAgentConfig? LoadConfig(IKVDataService kvData, string guid)
        {
            var json = kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Guid.ToString() == guid);
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
