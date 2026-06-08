using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public class BackgroundGenerationConfig : ModifierConfig
    {
        [UiHint("SubAgent 配置名", "选择后台运行的 SubAgent")]
        public string ConfigName { get; set; } = "";

        [UiHint("输出 ID", "CollectResults 中使用的 ID")]
        public string OutputId { get; set; } = "";
    }

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
            Description = "Run sub-agents in background concurrently."
        };

        public Type ConfigType => typeof(BackgroundGenerationConfig);

        public (bool IsValid, string Error) Validate(ModifierConfig config)
        {
            var cfg = (BackgroundGenerationConfig)config;
            if (string.IsNullOrWhiteSpace(cfg.ConfigName))
                return (false, "ConfigName cannot be empty");
            return (true, "");
        }

        public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
        {
            var cfg = (BackgroundGenerationConfig)config;

            var subAgentConfig = LoadConfig(cfg.ConfigName);
            if (subAgentConfig == null)
                throw new InvalidOperationException($"SubAgent config '{cfg.ConfigName}' not found.");

            var apiSetting = SubAgentRunner.GetApiSetting(subAgentConfig.SelectedApiIndex, _kvData);
            var llmApi = apiSetting.LLMApi;
            var toolDefinitions = SubAgentRunner.GetToolDefinitions(subAgentConfig, _toolService);

            var basePb = new PromptBuilder { Messages = context.GetMessages() };
            var baseClone = SubAgentRunner.CreateBaseClone(basePb);
            var subChat = SubAgentRunner.CreateSubChat(subAgentConfig.Name);

            var subAgent = Agent.Create(subAgentConfig.Name, agent.Description, "");
            subAgent.Guid = subAgentConfig.Guid;
            subAgent.CustomToolNames = subAgentConfig.EnabledToolNames;

            var outputId = string.IsNullOrWhiteSpace(cfg.OutputId) ? cfg.ConfigName : cfg.OutputId;

            var task = Task.Run(async () =>
            {
                var outputMessages = await SubAgentRunner.RunAsync(
                    llmApi, apiSetting, baseClone, subAgentConfig, toolDefinitions,
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
