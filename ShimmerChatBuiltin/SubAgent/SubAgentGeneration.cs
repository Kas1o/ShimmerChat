using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public class SubAgentGeneration : IContextModifier
    {
        private const int MaxIterations = 50;
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

            var apiSetting = GetApiSetting(config.SelectedApiIndex);
            var llmApi = apiSetting.LLMApi;
            var toolDefinitions = GetToolDefinitions(config);

            var baseMessages = promptBuilder.Messages
                .Select(m => (DeepCloneMessage(m.Item1), m.Item2))
                .ToArray();

            var subChat = new Chat { Name = config.Name, Guid = Guid.NewGuid() };
            foreach (var (msg, from) in baseMessages)
            {
                subChat.Messages.Add(new Message
                {
                    message = msg,
                    timestamp = DateTime.UtcNow,
                    sender = from switch
                    {
                        PromptBuilder.From.user => Sender.User,
                        PromptBuilder.From.assistant => Sender.AI,
                        PromptBuilder.From.tool_result => Sender.ToolResult,
                        _ => Sender.System
                    }
                });
            }

            var subAgent = Agent.Create(config.Name, agent.description, "");
            subAgent.guid = config.Guid;
            subAgent.CustomToolNames = config.EnabledToolNames;

            var baseClone = new PromptBuilder(promptBuilder);
            baseClone.Messages = baseMessages;

            var outputMessages = Task.Run(async () =>
                await RunGenerationLoopAsync(llmApi, apiSetting, baseClone, config, toolDefinitions, subAgent, subChat))
                .GetAwaiter().GetResult();

            var outputText = FormatOutput(outputMessages, config.OutputMode);

            var parentMessages = promptBuilder.Messages.ToList();
            parentMessages.Add((new ChatMessage { Content = outputText }, PromptBuilder.From.assistant));
            promptBuilder.Messages = parentMessages.ToArray();
        }

        private async Task<List<(ChatMessage, PromptBuilder.From)>> RunGenerationLoopAsync(
            ILLMAPI llmApi,
            ApiSetting apiSetting,
            PromptBuilder baseClone,
            SubAgentConfig config,
            List<SharperLLM.FunctionCalling.Tool> toolDefinitions,
            Agent subAgent,
            Chat subChat)
        {
            var allMessages = new List<(ChatMessage, PromptBuilder.From)>();
            allMessages.AddRange(baseClone.Messages);
            var initialCount = allMessages.Count;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var fresh = BuildFreshPromptBuilder(baseClone, allMessages, toolDefinitions, subChat, subAgent, config);

                ResponseEx accumulatedResponse;
                if (apiSetting.EnableStream)
                {
                    accumulatedResponse = new ResponseEx { Body = new ChatMessage { Content = "" }, FinishReason = FinishReason.None };
                    var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    try
                    {
                        await foreach (var chunk in llmApi.GenerateChatExStream(fresh, cts.Token))
                        {
                            accumulatedResponse += chunk;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                }
                else
                {
                    accumulatedResponse = await llmApi.GenerateChatEx(fresh);
                }

                allMessages.Add((accumulatedResponse.Body, PromptBuilder.From.assistant));

                if (accumulatedResponse.FinishReason == FinishReason.FunctionCall
                    && accumulatedResponse.Body.toolCalls != null
                    && accumulatedResponse.Body.toolCalls.Count > 0)
                {
                    foreach (var toolCall in accumulatedResponse.Body.toolCalls)
                    {
                        var toolResult = await _toolService.ExecuteToolAsync(
                            toolCall.name, toolCall.arguments ?? "", subChat, subAgent);
                        allMessages.Add((new ChatMessage
                        {
                            Content = toolResult ?? "",
                            id = toolCall.id
                        }, PromptBuilder.From.tool_result));
                    }
                }
                else
                {
                    break;
                }
            }

            return allMessages.Skip(initialCount).ToList();
        }

        private PromptBuilder BuildFreshPromptBuilder(
            PromptBuilder baseClone,
            List<(ChatMessage, PromptBuilder.From)> allMessages,
            List<SharperLLM.FunctionCalling.Tool> toolDefinitions,
            Chat subChat,
            Agent subAgent,
            SubAgentConfig config)
        {
            var fresh = new PromptBuilder(baseClone);
            fresh.Messages = allMessages.ToArray();
            fresh.AvailableTools = toolDefinitions;
            fresh.AvailableToolsFormatter = ToolPromptParser.Parse;

            if (config.EnabledModifiers.Count > 0)
            {
                var modifierService = _serviceProvider.GetService(typeof(IContextModifierService)) as IContextModifierService;
                if (modifierService != null)
                {
                    foreach (var modConfig in config.EnabledModifiers)
                    {
                        var modifier = modifierService.LoadedModifiers
                            .FirstOrDefault(m => m.info.Name == modConfig.Name);
                        modifier?.ModifyContext(fresh, modConfig.Input, subChat, subAgent);
                    }
                }
            }

            return fresh;
        }

        private static ChatMessage DeepCloneMessage(ChatMessage original)
        {
            return new ChatMessage
            {
                Content = original.Content,
                thinking = original.thinking,
                id = original.id,
                ImageBase64 = original.ImageBase64,
                CustomProperties = original.CustomProperties != null
                    ? new Dictionary<string, object>(original.CustomProperties)
                    : null,
                toolCalls = original.toolCalls?.Select(tc => new SharperLLM.API.ToolCall
                {
                    name = tc.name,
                    id = tc.id,
                    arguments = tc.arguments,
                    index = tc.index
                }).ToList()
            };
        }

        private SubAgentConfig? LoadConfig(string name)
        {
            var json = _kvData.Read("SubAgent", "configs");
            var configs = JsonConvert.DeserializeObject<List<SubAgentConfig>>(json ?? "[]") ?? [];
            return configs.FirstOrDefault(c => c.Name == name);
        }

        private ApiSetting GetApiSetting(int index)
        {
            var json = _kvData.Read("ApiSettings", "apiSetting") ?? "[]";
            var settings = JsonConvert.DeserializeObject<List<ApiSetting>>(json) ?? [];
            if (settings.Count == 0)
                throw new InvalidOperationException("No API settings configured.");
            if (index == -1)
            {
                var globalIndexStr = _kvData.Read("ApiSettings", "selectedAPIIndex") ?? "0";
                index = int.Parse(globalIndexStr);
            }
            if (index >= 0 && index < settings.Count)
                return settings[index];
            return settings[0];
        }

        private List<SharperLLM.FunctionCalling.Tool> GetToolDefinitions(SubAgentConfig config)
        {
            var tools = new List<SharperLLM.FunctionCalling.Tool>();
            foreach (var toolName in config.EnabledToolNames)
            {
                var tool = _toolService.LoadedTools.FirstOrDefault(t => t.GetToolDefinition().name == toolName);
                if (tool != null)
                    tools.Add(tool.GetToolDefinition());
            }
            return tools;
        }

        private string FormatOutput(List<(ChatMessage, PromptBuilder.From)> messages, string outputMode)
        {
            if (messages.Count == 0)
                return "";

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
    }
}
