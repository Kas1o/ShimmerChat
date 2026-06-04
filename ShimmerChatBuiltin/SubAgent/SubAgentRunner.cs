using Newtonsoft.Json;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent
{
    public static class SubAgentRunner
    {
        private const int MaxIterations = 50;

        public static PromptBuilder CreateBaseClone(PromptBuilder parentPb)
        {
            var baseMessages = parentPb.Messages
                .Select(m => (DeepCloneMessage(m.Item1), m.Item2))
                .ToArray();
            var clone = new PromptBuilder(parentPb)
            {
                Messages = baseMessages
            };
            return clone;
        }

        public static Chat CreateSubChat(string name)
        {
            return new Chat { Name = name, Guid = Guid.NewGuid() };
        }

        public static ApiSetting GetApiSetting(int index, IKVDataService kvData)
        {
            var json = kvData.Read("ApiSettings", "apiSetting") ?? "[]";
            var settings = JsonConvert.DeserializeObject<List<ApiSetting>>(json) ?? [];
            if (settings.Count == 0)
                throw new InvalidOperationException("No API settings configured.");
            if (index == -1)
            {
                var globalIndexStr = kvData.Read("ApiSettings", "selectedAPIIndex") ?? "0";
                index = int.Parse(globalIndexStr);
            }
            if (index >= 0 && index < settings.Count)
                return settings[index];
            return settings[0];
        }

        public static List<SharperLLM.FunctionCalling.Tool> GetToolDefinitions(SubAgentConfig config, IToolService toolService)
        {
            var tools = new List<SharperLLM.FunctionCalling.Tool>();
            foreach (var toolName in config.EnabledToolNames)
            {
                var tool = toolService.LoadedTools.FirstOrDefault(t => t.GetToolDefinition().name == toolName);
                if (tool != null)
                    tools.Add(tool.GetToolDefinition());
            }
            return tools;
        }

        public static async Task<List<(ChatMessage, PromptBuilder.From)>> RunAsync(
            ILLMAPI llmApi,
            ApiSetting apiSetting,
            PromptBuilder baseClone,
            SubAgentConfig config,
            List<SharperLLM.FunctionCalling.Tool> toolDefinitions,
            Agent subAgent,
            Chat subChat,
            IToolService toolService,
            IServiceProvider serviceProvider)
        {
            var allMessages = new List<(ChatMessage, PromptBuilder.From)>();
            allMessages.AddRange(baseClone.Messages);
            var initialCount = allMessages.Count;

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var fresh = BuildPrompt(baseClone, allMessages, toolDefinitions, subChat, subAgent, config, serviceProvider);

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
                        var toolResult = await toolService.ExecuteToolAsync(
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

        private static PromptBuilder BuildPrompt(
            PromptBuilder baseClone,
            List<(ChatMessage, PromptBuilder.From)> allMessages,
            List<SharperLLM.FunctionCalling.Tool> toolDefinitions,
            Chat subChat,
            Agent subAgent,
            SubAgentConfig config,
            IServiceProvider serviceProvider)
        {
            var fresh = new PromptBuilder(baseClone)
            {
                Messages = allMessages.ToArray()
            };

            if (toolDefinitions.Count > 0)
            {
                fresh.AvailableTools = toolDefinitions;
                fresh.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            List<ActivatedModifier>? activeModifiers = null;

            if (config.UseSharedPreset && !string.IsNullOrEmpty(config.ModifierPresetId))
            {
                var modifierService = serviceProvider.GetService(typeof(IContextModifierService)) as IContextModifierService;
                activeModifiers = modifierService?.Presets
                    .FirstOrDefault(p => p.Id == config.ModifierPresetId)?.Modifiers;
            }
            else if (!config.UseSharedPreset && config.IndependentModifiers.Count > 0)
            {
                var modifierService = serviceProvider.GetService(typeof(IContextModifierService)) as IContextModifierService;
                activeModifiers = config.IndependentModifiers;
            }

            if (activeModifiers != null && activeModifiers.Count > 0)
            {
                var modifierService = serviceProvider.GetService(typeof(IContextModifierService)) as IContextModifierService;

                var context = new ContextDocument
                {
                    Segments = allMessages.Select(m => new ContextSegment
                    {
                        Message = m.Item1,
                        From = m.Item2
                    }).ToList()
                };

                foreach (var activatedModifier in activeModifiers.Where(m => m.IsEnabled))
                {
                    var modifier = modifierService?.LoadedModifiers
                        .FirstOrDefault(m => m.info.Name == activatedModifier.Name);
                    modifier?.ModifyContext(context, activatedModifier.Config, subChat, subAgent);
                }

                fresh.Messages = context.GetMessages();
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
    }
}
