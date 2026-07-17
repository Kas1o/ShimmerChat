using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    [NodeInfo("node.post_sub_agent", Icon = "🤖", Color = "var(--node-subagent)", CategoryKeys = ["category.flow", "category.sub_agent", "category.post"])]
    public class PostSubAgentNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Post SubAgent";

        [NodeProperty("prop.post_sub_agent.config_name", HintKey = "prop.post_sub_agent.config_name.hint")]
        public string ConfigName { get; set; } = "";

        [NodeProperty("prop.post_sub_agent.output_mode", HintKey = "prop.post_sub_agent.output_mode.hint")]
        public string OutputMode { get; set; } = "";

        [NodeProperty("prop.post_sub_agent.max_iterations", HintKey = "prop.post_sub_agent.max_iterations.hint")]
        public int MaxIterations { get; set; } = 50;

        [NodeProperty("prop.post_sub_agent.include_full_context", HintKey = "prop.post_sub_agent.include_full_context.hint")]
        public bool IncludeFullContext { get; set; } = false;

        [NodeProperty("prop.post_sub_agent.shared_guid", HintKey = "prop.post_sub_agent.shared_guid.hint")]
        public bool SharedGuid { get; set; } = false;

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            if (string.IsNullOrWhiteSpace(ConfigName))
                return PostNodeResult.SuccessResult();

            var kvData = context.Env.Persistent.KVData;
            var config = LoadConfig(kvData, ConfigName);
            if (config == null)
                return Fail(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_config_not_found", ConfigName));

            var rootNode = ResolveTree(config, kvData, context.Env.Persistent.Serializer);
            if (rootNode == null)
                return Fail(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.subagent_no_tree", ConfigName));

            // 1. 创建隔离的 PreGenerationEnv，构建聊天历史
            var persistent = new PersistentEnv
            {
                KVData = kvData,
                ChatGuid = context.Env.Persistent.ChatGuid,
                AgentGuid = SharedGuid ? context.Env.Persistent.AgentGuid : config.Guid,
                ToolRegistry = context.Env.Persistent.ToolRegistry,
                Serializer = context.Env.Persistent.Serializer,
                LocService = context.Env.Persistent.LocService,
                DebugOutput = context.Env.Persistent.DebugOutput,
                PostGenerationManager = context.Env.Persistent.PostGenerationManager
            };

            var subEnv = new PreGenerationEnv(persistent);

            var chatMessages = new List<Message>();

            if (IncludeFullContext)
            {
                // 传入完整上下文：将 PreFragments 映射为 Message 列表
                foreach (var seg in context.Env.PreFragments)
                {
                    chatMessages.Add(new Message
                    {
                        message = seg.Message,
                        sender = FragmentFromToSender(seg.From),
                        timestamp = DateTime.Now
                    });
                }
            }

            // 始终将当前 ResponseText 作为末尾用户消息追加
            chatMessages.Add(new Message
            {
                message = new ChatMessage { Content = context.Env.ResponseText },
                sender = Sender.User,
                timestamp = DateTime.Now
            });

            subEnv.Transient.SharedState["ChatMessages"] = chatMessages;

            // 2. 执行 SubAgent 修饰器树
            try
            {
                var ctx = new PreNodeExecutionContext(subEnv, context.CancellationToken);
                var result = await rootNode.ExecuteAsync(ctx);
                if (!result.Success)
                    return Fail(NodeErrorCodes.ServiceError,
                        loc.Format("node_err.subagent_tree_failed", result.Message));
            }
            catch (Exception ex)
            {
                return Fail(NodeErrorCodes.ServiceError,
                    loc.Format("node_err.subagent_tree_failed", ex.Message));
            }

            // 3. API：由修饰器树中的 APISelectNode 设置
            if (subEnv.Transient.API == null)
                return Fail(NodeErrorCodes.ApiUnavailable, loc["node_err.subagent_no_api"]);

            // 4. Tool Call 循环（隔离 env 内独立运行），每轮 assistant 响应经由 Post-Generation 管线
            var tools = subEnv.Transient.Tools;
            var toolDefs = tools.Select(t => t.GetDefinition()).ToList();
            var toolExecutor = new SubAgent.ToolV2Executor(tools);
            var promptCtx = new SubAgent.SubAgentPromptContext(
                subEnv.Transient.Fragments.Select(s => (s.Message, s.From)));

            Func<ChatMessage, Task<ChatMessage>>? postProcessor = null;
            if (!string.IsNullOrEmpty(config.PostGenerationTreeJson)
                && persistent.PostGenerationManager != null)
            {
                var postAgent = Agent.Create("__sub_post__", "");
                postAgent.PostGenerationTreeJson = config.PostGenerationTreeJson;
                var fragments = subEnv.Transient.Fragments;
                var mgr = persistent.PostGenerationManager;
                var ct = context.CancellationToken;
                var cfgName = ConfigName;
                var debug = context.Env.Persistent.DebugOutput;
                postProcessor = async msg =>
                {
                    try
                    {
                        return await mgr.ExecuteAsync(postAgent, msg, fragments, persistent, ct);
                    }
                    catch (Exception ex)
                    {
                        debug.Write("PostSubAgentNode", "PostGeneration",
                            $"[PostSubAgentNode] Post-Generation failed for '{cfgName}': {ex.Message}");
                        return msg;
                    }
                };
            }

            var host = new SubAgent.SubAgentLoopHost(promptCtx, toolExecutor, postProcessor);
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
                return Fail(NodeErrorCodes.ServiceError,
                    $"[SubAgent Error: {ex.Message}]");
            }

            // 5. 输出格式化，写回 ResponseText
            var mode = !string.IsNullOrWhiteSpace(OutputMode) ? OutputMode : config.OutputMode;
            if (mode == "None")
                return PostNodeResult.SuccessResult();

            var outputText = SubAgent.SubAgentFormatter.Format(mode, promptCtx);
            if (!string.IsNullOrEmpty(outputText))
            {
                context.Env.ResponseText = outputText;
            }

            return PostNodeResult.SuccessResult();
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

        private PostNodeResult Fail(string code, string message)
        {
            var r = PostNodeResult.Failure(code, message);
            r.NodeId = Id;
            r.NodeName = Name;
            return r;
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
