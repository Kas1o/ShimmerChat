using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public class BackgroundGeneration : IContextModifier
    {
        private readonly IKVDataService _kvData;
        private readonly IToolService _toolService;
        private readonly IServiceProvider _serviceProvider;

        public BackgroundGeneration(IKVDataService kvData, IToolService toolService, IServiceProvider serviceProvider)
        {
            _kvData = kvData;
            _toolService = toolService;
            _serviceProvider = serviceProvider;
        }

        public ContextModifierInfo info => new()
        {
            Name = "BackgroundGeneration",
            Description = "Run sub-agents in background concurrently. Input: configName, outputId"
        };

        public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
        {
            var parts = input.Split(',', 2);
            var configName = parts[0].Trim();
            var outputId = parts.Length > 1 ? parts[1].Trim() : configName;

            var config = LoadConfig(configName);
            if (config == null)
                throw new InvalidOperationException($"SubAgent config '{configName}' not found.");

            var apiSetting = SubAgentRunner.GetApiSetting(config.SelectedApiIndex, _kvData);
            var llmApi = apiSetting.LLMApi;
            var toolDefinitions = SubAgentRunner.GetToolDefinitions(config, _toolService);

            var baseClone = SubAgentRunner.CreateBaseClone(promptBuilder);
            var subChat = SubAgentRunner.CreateSubChat(config.Name);

            var subAgent = Agent.Create(config.Name, agent.description, "");
            subAgent.guid = config.Guid;
            subAgent.CustomToolNames = config.EnabledToolNames;

            var task = Task.Run(async () =>
            {
                var outputMessages = await SubAgentRunner.RunAsync(
                    llmApi, apiSetting, baseClone, config, toolDefinitions,
                    subAgent, subChat, _toolService, _serviceProvider);
                return outputMessages.Count == 0 ? "" : outputMessages[^1].Item1.Content;
            });

            SubAgentResultStore.Put(outputId, task);
        }

        private SubAgentConfig? LoadConfig(string name)
        {
            var json = _kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }
    }
}
