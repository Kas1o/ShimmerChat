using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IToolV2 版本的 SubAgent 工具，允许 LLM 通过 Tool Call 主动唤起 SubAgent。
    /// 依赖由 SubAgentToolNode 通过构造函数注入。
    /// </summary>
    public class SubAgentToolV2 : IToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly ILLMAPI? _api;
        private readonly List<IToolV2> _tools;

        public SubAgentToolV2(IKVDataService kvData, ILLMAPI? api, List<IToolV2> tools)
        {
            _kvData = kvData;
            _api = api;
            _tools = tools;
        }

        public Tool GetDefinition() => new()
        {
            name = "subagent_call",
            description = "Invoke a sub-agent with a specific config name to handle complex subtasks. The sub-agent will run independently with its own context and return results.",
            parameters =
            [
                (new ToolParameter { name = "config_name", type = ParameterType.String, description = "The name of the SubAgent configuration to invoke." }, true),
                (new ToolParameter { name = "task", type = ParameterType.String, description = "The task description for the sub-agent." }, true)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            var args = JsonConvert.DeserializeObject<SubAgentCallArgs>(input);
            if (args == null || string.IsNullOrWhiteSpace(args.config_name))
                return "Error: config_name is required.";

            if (_api == null)
                return "Error: No API available for SubAgent.";

            var config = LoadConfig(_kvData, args.config_name);
            if (config == null)
                return $"SubAgent config '{args.config_name}' not found.";

            var toolDefs = _tools.Select(t => t.GetDefinition()).ToList();

            var messages = new List<(ChatMessage, PromptBuilder.From)>
            {
                (new ChatMessage { Content = args.task ?? "Execute the configured task." }, PromptBuilder.From.user)
            };

            for (int i = 0; i < 50; i++)
            {
                var pb = new PromptBuilder
                {
                    Messages = messages.ToArray(),
                    AvailableTools = toolDefs.Count > 0 ? toolDefs : null,
                    AvailableToolsFormatter = toolDefs.Count > 0 ? ToolPromptParser.Parse : null
                };

                ResponseEx response;
                try { response = await _api.GenerateChatEx(pb); }
                catch (Exception ex) { return $"[SubAgent Error: {ex.Message}]"; }

                messages.Add((response.Body, PromptBuilder.From.assistant));

                if (response.FinishReason == FinishReason.FunctionCall
                    && response.Body.toolCalls?.Count > 0)
                {
                    foreach (var tc in response.Body.toolCalls)
                    {
                        var tool = _tools.FirstOrDefault(t => t.GetDefinition().name == tc.name);
                        var result = tool != null
                            ? await tool.ExecuteAsync(tc.arguments ?? "{}")
                            : $"Tool '{tc.name}' not found.";
                        messages.Add((new ChatMessage { Content = result, id = tc.id }, PromptBuilder.From.tool_result));
                    }
                }
                else break;
            }

            return messages.LastOrDefault(m => m.Item2 == PromptBuilder.From.assistant).Item1?.Content
                ?? "No response from sub-agent.";
        }

        private static SubAgentConfig? LoadConfig(IKVDataService kvData, string name)
        {
            var json = kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }

        private class SubAgentCallArgs
        {
            [JsonProperty("config_name")] public string? config_name { get; set; }
            [JsonProperty("task")] public string? task { get; set; }
        }
    }
}
