using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.SubAgent;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// SubAgent 节点。在修改器阶段调用 SubAgent 运行独立生成，
    /// 结果注入到上下文片段中。
    /// </summary>
    [NodeInfo("SubAgent", Icon = "🤖", Color = "#e06090", Category = "Flow/SubAgent")]
    public class SubAgentNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "SubAgent";

        /// <summary>
        /// SubAgent 配置名称
        /// </summary>
        [NodeProperty("Config Name", Hint = "Name of the SubAgent configuration")]
        public string ConfigName { get; set; } = "";

        /// <summary>
        /// 输出模式：LastMessage / FullJson / None
        /// </summary>
        [NodeProperty("Output Mode", Hint = "LastMessage, FullJson, or None")]
        public string OutputMode { get; set; } = "LastMessage";

        /// <summary>
        /// 最大迭代次数
        /// </summary>
        [NodeProperty("Max Iterations", Hint = "Maximum tool-call loop iterations")]
        public int MaxIterations { get; set; } = 50;

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(ConfigName))
                return NodeResult.SuccessResult();

            var kvData = context.Env.Persistent.KVData;
            var agent = context.Env.Persistent.GetAgent();

            var config = LoadSubAgentConfig(kvData, ConfigName);
            if (config == null)
                return NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    $"SubAgent: Config '{ConfigName}' not found.",
                    nodeId: Id, nodeName: Name);

            if (context.Env.Transient.API == null)
                return NodeResult.Failure(
                    NodeErrorCodes.ApiUnavailable,
                    "SubAgent: No API configured.",
                    nodeId: Id, nodeName: Name);

            var api = context.Env.Transient.API;

            var toolDefinitions = context.Env.Transient.Tools
                .Select(t => t.GetDefinition())
                .ToList();

            var baseMessages = context.Env.Transient.Fragments
                .Select(s => (s.Message, s.From))
                .ToArray();

            var allMessages = new List<(ChatMessage, PromptBuilder.From)>(baseMessages);

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var pb = new PromptBuilder
                {
                    Messages = allMessages.ToArray(),
                    AvailableTools = toolDefinitions.Count > 0 ? toolDefinitions : null,
                    AvailableToolsFormatter = toolDefinitions.Count > 0 ? ToolPromptParser.Parse : null
                };

                ResponseEx response;
                try
                {
                    response = await api.GenerateChatEx(pb);
                }
                catch (Exception ex)
                {
                    allMessages.Add((new ChatMessage { Content = $"[SubAgent Error: {ex.Message}]" }, PromptBuilder.From.assistant));
                    break;
                }

                allMessages.Add((response.Body, PromptBuilder.From.assistant));

                if (response.FinishReason == FinishReason.FunctionCall
                    && response.Body.toolCalls != null
                    && response.Body.toolCalls.Count > 0)
                {
                    foreach (var tc in response.Body.toolCalls)
                    {
                        var tool = context.Env.Transient.Tools
                            .FirstOrDefault(t => t.GetDefinition().name == tc.name);
                        string result = tool != null
                            ? await tool.ExecuteAsync(tc.arguments ?? "{}")
                            : $"Tool '{tc.name}' not found.";
                        allMessages.Add((new ChatMessage { Content = result, id = tc.id }, PromptBuilder.From.tool_result));
                    }
                }
                else
                {
                    break; // 没有 tool call，结束
                }
            }

            if (OutputMode == "None")
                return NodeResult.SuccessResult();

            var outputMessages = allMessages.Skip(baseMessages.Length).ToList();
            if (outputMessages.Count == 0)
                return NodeResult.SuccessResult();

            var outputText = FormatOutput(outputMessages);

            context.Env.Transient.Fragments.Add(new ContextSegment
            {
                SourceType = typeof(SubAgentNode),
                Message = new ChatMessage { Content = outputText },
                From = PromptBuilder.From.assistant,
                Metadata = new Dictionary<string, object> { ["subAgentName"] = ConfigName }
            });

            return NodeResult.SuccessResult();
        }

        private static string FormatOutput(List<(ChatMessage, PromptBuilder.From)> messages)
        {
            if (messages.Count == 0) return "";
            return messages[^1].Item1.Content;
        }

        private static SubAgentConfig? LoadSubAgentConfig(IKVDataService kvData, string name)
        {
            var json = kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]")
                ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }
    }
}
