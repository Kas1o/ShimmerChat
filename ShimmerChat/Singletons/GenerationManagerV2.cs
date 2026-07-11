using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Generation.Nodes;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// ShimmerChat 2.0 生成管道管理器。
    /// 执行修改器树 → 构建 Prompt → 调用 API → Tool Call 循环。
    /// Tool Call 循环委托给 ToolCallLoop，自身通过 MainLoopHost 适配。
    /// </summary>
    public class GenerationManagerV2
    {
        private readonly IKVDataService _kvData;
        private readonly IToolRegistry _toolRegistry;
        private readonly IGenerationNodeSerializer _serializer;
        private readonly GenerationTreeExecutor _executor = new();
        private readonly ToolCallLoop _loop = new();

        public GenerationManagerV2(IKVDataService kvData, IToolRegistry toolRegistry,
            IGenerationNodeSerializer serializer)
        {
            _kvData = kvData;
            _toolRegistry = toolRegistry;
            _serializer = serializer;
            EnsureDefaultPreset();
        }

        private void EnsureDefaultPreset()
        {
            var json = _kvData.Read("GenerationManager", "generation_presets");
            var presets = string.IsNullOrEmpty(json)
                ? new List<GenerationPreset>()
                : (JsonConvert.DeserializeObject<List<GenerationPreset>>(json) ?? new List<GenerationPreset>());

            presets.RemoveAll(p => p.Id == "__default__");

            presets.Add(new GenerationPreset
            {
                Id = "__default__",
                Name = "Default",
                RootNodeJson = _serializer.Serialize(new SequenceNode
                {
                    Name = "Default",
                    Nodes = new List<IGenerationNode>
                    {
                        new FragmentNode
                        {
                            Name = "System Prompt",
                            Content = "You are a helpful AI assistant.",
                            From = PromptBuilder.From.system
                        },
                        new AppendChatMessagesNode { Name = "Append Chat Messages" },
                        new APISelectNode { Name = "Select API", APIIndex = -1 },
                        new ToolPresetNode { Name = "Load Tools", PresetName = "" }
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
            Action<List<ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken)
        {
            var env = await BuildEnvironment(agent, chat, cancellationToken);

            var host = new MainLoopHost(this, agent, chat, env,
                onStreamDelta, onAssistantComplete, onToolCall, onToolResult,
                cancellationToken);

            var api = env.Transient.API
                ?? throw new InvalidOperationException("No API configured.");

            await _loop.RunAsync(
                api,
                env.Transient.Tools.Select(t => t.GetDefinition()).ToList(),
                host,
                ct: cancellationToken);
        }

        /// <summary>
        /// 流式继续生成（给最后一条 AI 消息追加 prefix: true 参数）
        /// </summary>
        public async Task GenerateContinuationStreamAsync(
            Agent agent, Chat chat, Message continuationMessage,
            Func<ResponseEx, Task> onStreamDelta,
            Func<ResponseEx, Task> onAssistantComplete,
            CancellationToken cancellationToken)
        {
            continuationMessage.message.CustomProperties ??= new Dictionary<string, object>();
            continuationMessage.message.CustomProperties["prefix"] = true;

            var env = await BuildEnvironment(agent, chat, cancellationToken);

            var host = new MainLoopHost(this, agent, chat, env,
                onStreamDelta, onAssistantComplete, null, null,
                cancellationToken);

            var api = env.Transient.API
                ?? throw new InvalidOperationException("No API configured.");

            await _loop.RunAsync(
                api,
                env.Transient.Tools.Select(t => t.GetDefinition()).ToList(),
                host,
                ct: cancellationToken);
        }

        /// <summary>
        /// 非流式生成 AI 响应
        /// </summary>
        public async Task GenerateAsync(
            Agent agent,
            Chat chat,
            Action<ResponseEx> onResponse,
            Action<(string name, string resp, string id)> onToolResult)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await GenerateStreamAsync(
                agent, chat,
                onStreamDelta: rsp => { onResponse(rsp); return Task.CompletedTask; },
                onAssistantComplete: _ => Task.CompletedTask,
                onToolCall: null!,
                onToolResult: onToolResult,
                cancellationToken: cts.Token);
        }

        /// <summary>
        /// 构建生成环境：执行修改器树 + 加载历史消息
        /// </summary>
        public async Task<GenerationEnv> BuildEnvironment(
            Agent agent, Chat chat, CancellationToken ct)
        {
            var persistent = new PersistentEnv
            {
                KVData = _kvData,
                ChatGuid = chat.Guid,
                AgentGuid = agent.Guid,
                ToolRegistry = _toolRegistry,
                Serializer = _serializer
            };

            IGenerationNode rootNode;
            if (!string.IsNullOrEmpty(agent.ModifierTreeJson))
            {
                rootNode = _serializer.Deserialize(agent.ModifierTreeJson)
                    ?? CreateFallbackRoot(agent);
            }
            else
            {
                rootNode = CreateFallbackRoot(agent);
            }

            var env = new GenerationEnv(persistent);
            env.Transient.SharedState["ChatMessages"] = chat.Messages.ToList();

            var context = new NodeExecutionContext(env, ct);
            var result = await rootNode.ExecuteAsync(context);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"Generation tree execution failed. Node: '{result.NodeName}' ({result.NodeId}). {result.Message}");

            return env;
        }

        /// <summary>
        /// IToolCallLoopHost 实现：桥接 ToolCallLoop 和 ShimmerChat 主循环。
        /// </summary>
        private class MainLoopHost : IToolCallLoopHost
        {
            private readonly GenerationManagerV2 _manager;
            private readonly Agent _agent;
            private readonly Chat _chat;
            private GenerationEnv _env;
            private readonly Func<ResponseEx, Task> _onStreamDelta;
            private readonly Func<ResponseEx, Task> _onAssistantComplete;
            private readonly Action<List<ToolCall>>? _onToolCall;
            private readonly Action<(string, string, string)>? _onToolResult;
            private readonly CancellationToken _ct;

            public MainLoopHost(
                GenerationManagerV2 manager,
                Agent agent,
                Chat chat,
                GenerationEnv env,
                Func<ResponseEx, Task> onStreamDelta,
                Func<ResponseEx, Task> onAssistantComplete,
                Action<List<ToolCall>>? onToolCall,
                Action<(string, string, string)>? onToolResult,
                CancellationToken ct)
            {
                _manager = manager;
                _agent = agent;
                _chat = chat;
                _env = env;
                _onStreamDelta = onStreamDelta;
                _onAssistantComplete = onAssistantComplete;
                _onToolCall = onToolCall;
                _onToolResult = onToolResult;
                _ct = ct;
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

            public Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
            {
                var tool = _env.Transient.Tools.FirstOrDefault(t => t.GetDefinition().name == toolCall.name);
                if (tool != null)
                    return tool.ExecuteAsync(toolCall.arguments ?? "{}");

                return Task.FromResult($"Error: Tool '{toolCall.name}' not found.");
            }

            public async Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
            {
                await _onStreamDelta(accumulated);
            }

            public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
            {
                await _onAssistantComplete(fullResponse);
                return true;
            }

            public async Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
            {
                _onToolResult?.Invoke((toolCall.name, result, toolCall.id ?? ""));

                // 重建 env，让 AppendChatMessagesNode 从 Chat 统一加载（handleStream 和 onToolResult 已持久化）
                _env = await _manager.BuildEnvironment(_agent, _chat, ct);
            }
        }

        private static IGenerationNode CreateFallbackRoot(Agent agent)
        {
            var root = new SequenceNode { Name = agent.Name };

            if (!string.IsNullOrWhiteSpace(agent.Description))
            {
                root.Nodes.Add(new FragmentNode
                {
                    Name = "System Prompt",
                    Content = agent.Description,
                    From = PromptBuilder.From.system
                });
            }

            root.Nodes.Add(new CallNode { PresetId = "__default__" });

            return root;
        }
    }
}
