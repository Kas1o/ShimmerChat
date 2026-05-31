using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentGeneration : IContextModifier
    {
        private readonly IKVDataService _kvData;
        private readonly IToolService _toolService;
        private readonly IServiceProvider _serviceProvider;

        public SubAgentGeneration(IKVDataService kvData, IToolService toolService, IServiceProvider serviceProvider)
        {
            _kvData = kvData;
            _toolService = toolService;
            _serviceProvider = serviceProvider;
        }

        public ContextModifierInfo info => new()
        {
            Name = "SubAgentGeneration",
            Description = "Run a sub-agent with independent configuration. Input: sub-agent config name."
        };

        public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
        {
            var config = LoadConfig(input);
            if (config == null)
                throw new InvalidOperationException($"SubAgent config '{input}' not found.");

            var apiSetting = SubAgentRunner.GetApiSetting(config.SelectedApiIndex, _kvData);
            var llmApi = apiSetting.LLMApi;
            var toolDefinitions = SubAgentRunner.GetToolDefinitions(config, _toolService);

            var baseClone = SubAgentRunner.CreateBaseClone(promptBuilder);
            var subChat = SubAgentRunner.CreateSubChat(config.Name);

            var subAgent = Agent.Create(config.Name, agent.description, "");
            subAgent.guid = config.Guid;
            subAgent.CustomToolNames = config.EnabledToolNames;

            var outputMessages = Task.Run(async () =>
                await SubAgentRunner.RunAsync(llmApi, apiSetting, baseClone, config, toolDefinitions, subAgent, subChat, _toolService, _serviceProvider))
                .GetAwaiter().GetResult();

            if (config.OutputMode == "None")
                return;

            var outputText = FormatOutput(outputMessages, config.OutputMode);

            var parentMessages = promptBuilder.Messages.ToList();
            parentMessages.Add((new ChatMessage { Content = outputText }, PromptBuilder.From.system));
            promptBuilder.Messages = parentMessages.ToArray();
        }

        private string FormatOutput(List<(ChatMessage, PromptBuilder.From)> messages, string outputMode)
        {
            if (messages.Count == 0) return "";

            return outputMode switch
            {
                "FullJson" => JsonConvert.SerializeObject(
                    messages.Select(m =>
                    {
                        var obj = new Dictionary<string, object>
                        {
                            ["role"] = m.Item2 switch
                            {
                                PromptBuilder.From.assistant => "assistant",
                                PromptBuilder.From.tool_result => "tool",
                                PromptBuilder.From.system => "system",
                                PromptBuilder.From.user => "user",
                                _ => m.Item2.ToString()
                            },
                            ["content"] = m.Item1.Content
                        };
                        if (m.Item1.toolCalls != null)
                            obj["tool_calls"] = m.Item1.toolCalls;
                        if (m.Item1.thinking != null)
                            obj["thinking"] = m.Item1.thinking;
                        if (m.Item1.id != null)
                            obj["tool_call_id"] = m.Item1.id;
                        return obj;
                    }),
                    Formatting.Indented),
                _ => messages[^1].Item1.Content
            };
        }

        private SubAgentConfig? LoadConfig(string name)
        {
            var json = _kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }
    }
}
