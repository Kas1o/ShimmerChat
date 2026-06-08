using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentGenerationConfig : ModifierConfig
    {
        [UiHint("SubAgent 配置", "选择已配置的 SubAgent")]
        public string ConfigName { get; set; } = "";
    }

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
            Description = "Run a sub-agent with independent configuration, tools, and API settings."
        };

        public Type ConfigType => typeof(SubAgentGenerationConfig);

        public (bool IsValid, string Error) Validate(ModifierConfig config)
        {
            var cfg = (SubAgentGenerationConfig)config;
            if (string.IsNullOrWhiteSpace(cfg.ConfigName))
                return (false, "ConfigName cannot be empty");
            return (true, "");
        }

        public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
        {
            var cfg = (SubAgentGenerationConfig)config;

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

            var outputMessages = Task.Run(async () =>
                await SubAgentRunner.RunAsync(llmApi, apiSetting, baseClone, subAgentConfig, toolDefinitions, subAgent, subChat, _toolService, _serviceProvider))
                .GetAwaiter().GetResult();

            if (subAgentConfig.OutputMode == "None")
                return;

            var outputText = FormatOutput(outputMessages, subAgentConfig.OutputMode);

            context.Segments.Add(new ContextSegment
            {
                SourceType = typeof(SubAgentGeneration),
                Message = new ChatMessage { Content = outputText },
                From = PromptBuilder.From.assistant,
                Metadata = new Dictionary<string, object>
                {
                    ["subAgentName"] = subAgentConfig.Name
                }
            });
        }

        private static string FormatOutput(List<(ChatMessage, PromptBuilder.From)> messages, string outputMode)
        {
            if (messages.Count == 0) return "";

            return outputMode switch
            {
                "FullJson" => JsonConvert.SerializeObject(
                    messages.Select(m => new
                    {
                        role = m.Item2 switch
                        {
                            PromptBuilder.From.assistant => "assistant",
                            PromptBuilder.From.tool_result => "tool_result",
                            _ => m.Item2.ToString()
                        },
                        content = m.Item1.Content,
                        tool_calls = m.Item1.toolCalls,
                        thinking = m.Item1.thinking
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
