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
    /// 替代 AIGenerationServiceV1：执行修改器树 → 构建 Prompt → 调用 API → Tool Call 循环。
    /// </summary>
    public class GenerationManagerV2
    {
        private readonly IKVDataService _kvData;
        private readonly IToolRegistry _toolRegistry;
        private readonly IGenerationNodeSerializer _serializer;
        private readonly GenerationTreeExecutor _executor = new();

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
                        new ToolPresetNode { Name = "Load Tools", PresetName = "_default_" }
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
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStream,
            Action<List<ToolCall>> onToolCall,
            Action<(string name, string resp, string id)> onToolResult,
            CancellationToken cancellationToken)
        {
            // 1. 解析并执行修改器树
            var env = await BuildEnvironment(agent, chat, cancellationToken);

            // 2. 构建初始 PromptBuilder
            var result = await RunToolCallLoop(env, handleStream, onToolCall, onToolResult, cancellationToken);
        }

        /// <summary>
        /// 流式继续生成（给最后一条 AI 消息追加 prefix: true 参数）
        /// </summary>
        public async Task GenerateContinuationStreamAsync(
            Agent agent, Chat chat, Message continuationMessage,
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStream,
            CancellationToken cancellationToken)
        {
            var env = await BuildEnvironment(agent, chat, cancellationToken);

            // Set prefix flag on the last assistant fragment
            var lastAssistant = env.Transient.Fragments.LastOrDefault(f => f.From == SharperLLM.Util.PromptBuilder.From.assistant);
            if (lastAssistant != null)
            {
                lastAssistant.Message.CustomProperties ??= new Dictionary<string, object>();
                lastAssistant.Message.CustomProperties["prefix"] = true;
            }

            await RunToolCallLoop(env, handleStream, null!, null!, cancellationToken);
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
                async stream =>
                {
                    ResponseEx accumulated = new ResponseEx
                    {
                        Body = new ChatMessage { Content = "" },
                        FinishReason = FinishReason.None
                    };
                    await foreach (var chunk in stream)
                    {
                        accumulated += chunk;
                        onResponse(accumulated);
                    }
                },
                null!, onToolResult, cts.Token);
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

            // 解析 Agent 的修改器树
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

            // 先构建空 env，将聊天历史放入 SharedState（由 AppendChatMessages 节点负责注入）
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
        /// 执行 Tool Call 循环
        /// </summary>
        private async Task<ResponseEx> RunToolCallLoop(
            GenerationEnv env,
            Func<IAsyncEnumerable<ResponseEx>, Task> handleStream,
            Action<List<ToolCall>> onToolCall,
            Action<(string, string, string)> onToolResult,
            CancellationToken ct)
        {
            var api = env.Transient.API
                ?? throw new InvalidOperationException("No API configured.");
            var tools = env.Transient.Tools;
            var fragments = env.Transient.Fragments;

            const int maxIterations = 50;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();

                var pb = BuildPromptBuilder(fragments, tools);

                // Accumulate stream into a single ResponseEx for tool detection,
                // while forwarding each accumulated step to the UI handler.
                var accumulated = new ResponseEx
                {
                    Body = new ChatMessage { Content = "" },
                    FinishReason = FinishReason.None
                };
                var raw = api.GenerateChatExStream(pb, ct);

                await handleStream(ForwardAccumulated(raw, accumulated));

                // ForwardAccumulated mutates accumulated in-place via the reference,
                // so accumulated now contains the final full response.
                
                fragments.Add(new ContextSegment
                {
                    Message = accumulated.Body,
                    From = PromptBuilder.From.assistant
                });

                // Tool Call 处理
                if (accumulated.FinishReason == FinishReason.FunctionCall
                    && accumulated.Body.toolCalls != null
                    && accumulated.Body.toolCalls.Count > 0)
                {
                    onToolCall?.Invoke(accumulated.Body.toolCalls);

                    foreach (var tc in accumulated.Body.toolCalls)
                    {
                        var tool = tools.FirstOrDefault(t => t.GetDefinition().name == tc.name);
                        string result;
                        if (tool != null)
                        {
                            result = await tool.ExecuteAsync(tc.arguments ?? "{}");
                        }
                        else
                        {
                            result = $"Error: Tool '{tc.name}' not found.";
                        }

                        fragments.Add(new ContextSegment
                        {
                            Message = new ChatMessage { Content = result, id = tc.id },
                            From = PromptBuilder.From.tool_result
                        });

                        onToolResult?.Invoke((tc.name, result, tc.id ?? ""));
                    }
                }
                else
                {
                    return accumulated;
                }
            }

            throw new InvalidOperationException("Tool call loop exceeded maximum iterations.");
        }

        private static PromptBuilder BuildPromptBuilder(
            List<ContextSegment> fragments, List<IToolV2> tools)
        {
            var pb = new PromptBuilder
            {
                Messages = fragments.Select(s => (s.Message, s.From)).ToArray()
            };

            if (tools.Count > 0)
            {
                pb.AvailableTools = tools.Select(t => t.GetDefinition()).ToList();
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            return pb;
        }

        private static async IAsyncEnumerable<ResponseEx> ForwardAccumulated(
            IAsyncEnumerable<ResponseEx> source, ResponseEx acc)
        {
            await foreach (var chunk in source)
            {
                // Mutate acc in-place so the caller sees the final result
                acc.Body.Content += chunk.Body.Content;
                acc.FinishReason = chunk.FinishReason;
                if (chunk.Body.thinking != null)
                    acc.Body.thinking = (acc.Body.thinking ?? "") + chunk.Body.thinking;
                if (chunk.Body.id != null)
                    acc.Body.id = chunk.Body.id;

                if (chunk.Body.toolCalls != null)
                {
                    acc.Body.toolCalls ??= new List<ToolCall>();
                    foreach (var tc in chunk.Body.toolCalls)
                    {
                        var existing = acc.Body.toolCalls.FirstOrDefault(t => t.index == tc.index);
                        if (existing != null)
                            existing.arguments = (existing.arguments ?? "") + (tc.arguments ?? "");
                        else
                            acc.Body.toolCalls.Add(new ToolCall
                            {
                                name = tc.name, id = tc.id,
                                arguments = tc.arguments ?? "", index = tc.index
                            });
                    }
                }
                yield return acc;
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
