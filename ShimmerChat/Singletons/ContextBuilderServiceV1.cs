using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Models;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    public class ContextBuilderServiceV1 : IContextBuilderService
    {
        private readonly IContextModifierService _contextModifierService;

        public ContextBuilderServiceV1(IContextModifierService contextModifierService)
        {
            _contextModifierService = contextModifierService;
        }

        private static ContextDocument CreateContextDocument(Chat chat, string system)
        {
            var segments = chat.Messages
                .Where(x => x.GenerationState != MessageGenerationState.Regenerating)
                .Select(x =>
                {
                    var from = x.sender.ToLower() switch
                    {
                        Sender.User => PromptBuilder.From.user,
                        Sender.System => PromptBuilder.From.system,
                        Sender.AI => PromptBuilder.From.assistant,
                        Sender.ToolResult => PromptBuilder.From.tool_result,
                        var n => throw new InvalidOperationException($"Unsupported sender Type: {n}")
                    };

                    return new ContextSegment
                    {
                        Message = CloneChatMessage(x.message),
                        From = from,
                        Metadata = new Dictionary<string, object>
                        {
                            ["timestamp"] = x.timestamp,
                            ["sender"] = x.sender
                        }
                    };
                }).ToList();

            segments.Insert(0, new ContextSegment
            {
                Message = system,
                From = PromptBuilder.From.system
            });

            return new ContextDocument { Segments = segments };
        }

        public ContextDocument BuildContextDocument(Chat chat, Agent agent)
        {
            var context = CreateContextDocument(chat, agent.description);
            _contextModifierService.ApplyModifiers(context, chat, agent);
            return context;
        }

        public ContextDocument BuildContextDocumentWithTools(Chat chat, Agent agent, List<Tool> toolDefinitions)
        {
            var context = CreateContextDocument(chat, agent.description);
            _contextModifierService.ApplyModifiers(context, chat, agent);
            return context;
        }

        public PromptBuilder BuildPromptBuilder(Chat chat, Agent agent)
        {
            var context = BuildContextDocument(chat, agent);
            var pb = new PromptBuilder { Messages = context.GetMessages() };
            return pb;
        }

        public PromptBuilder BuildPromptBuilderWithTools(Chat chat, Agent agent, List<Tool> toolDefinitions)
        {
            var context = BuildContextDocument(chat, agent);
            var pb = new PromptBuilder { Messages = context.GetMessages() };

            if (toolDefinitions != null && toolDefinitions.Count > 0)
            {
                pb.AvailableTools = toolDefinitions;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            return pb;
        }

        public PromptBuilder BuildPromptBuilderWithoutContextModify(Chat chat, Agent agent)
        {
            var pb = new PromptBuilder();
            var p = chat.Messages
                .Where(x => x.GenerationState != MessageGenerationState.Regenerating)
                .Select(
                    x =>
                        x.sender.ToLower() switch
                        {
                            Sender.User => (x.message, PromptBuilder.From.user),
                            Sender.System => (x.message, PromptBuilder.From.system),
                            Sender.AI => (x.message, PromptBuilder.From.assistant),
                            Sender.ToolResult => (x.message, PromptBuilder.From.tool_result),
                            var n => throw new InvalidOperationException($"Unsupported sender Type: {n}")
                        }
                ).ToList();
            p.Insert(0, (agent.description, PromptBuilder.From.system));
            pb.Messages = p.ToArray();
            return pb;
        }

        public PromptBuilder BuildPromptBuilderForContinuation(Chat chat, Agent agent, List<Tool> toolDefinitions, Message continuationMessage)
        {
            var pb = new PromptBuilder();
            var p = chat.Messages
                .Where(x => x.GenerationState != MessageGenerationState.Regenerating)
                .Select(
                    x =>
                    {
                        var from = x.sender.ToLower() switch
                        {
                            Sender.User => PromptBuilder.From.user,
                            Sender.System => PromptBuilder.From.system,
                            Sender.AI => PromptBuilder.From.assistant,
                            Sender.ToolResult => PromptBuilder.From.tool_result,
                            var n => throw new InvalidOperationException($"Unsupported sender Type: {n}")
                        };

                        var message = x.message;
                        if (x == continuationMessage && x.sender == Sender.AI)
                        {
                            message = new ChatMessage
                            {
                                Content = x.message?.Content ?? string.Empty,
                                thinking = null,
                                toolCalls = x.message?.toolCalls,
                                CustomProperties = new Dictionary<string, object>(x.message?.CustomProperties ?? new Dictionary<string, object>())
                            };
                            message.CustomProperties["prefix"] = true;
                        }

                        return (message, from);
                    }
                ).ToList();
            p.Insert(0, (agent.description, PromptBuilder.From.system));
            pb.Messages = p.ToArray();
            if (toolDefinitions != null && toolDefinitions.Count > 0)
            {
                pb.AvailableTools = toolDefinitions;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            var context = new ContextDocument
            {
                Segments = p.Select(m => new ContextSegment
                {
                    Message = CloneChatMessage(m.Item1),
                    From = m.Item2
                }).ToList()
            };
            _contextModifierService.ApplyModifiers(context, chat, agent);
            pb.Messages = context.GetMessages();

            return pb;
        }

        private static ChatMessage CloneChatMessage(ChatMessage original)
        {
            return new ChatMessage
            {
                Content = original.Content,
                ImageBase64 = original.ImageBase64,
                thinking = original.thinking,
                id = original.id,
                toolCalls = original.toolCalls?.Select(tc => new SharperLLM.API.ToolCall
                {
                    name = tc.name,
                    id = tc.id,
                    arguments = tc.arguments,
                    index = tc.index
                }).ToList(),
                CustomProperties = original.CustomProperties != null
                    ? new Dictionary<string, object>(original.CustomProperties)
                    : null
            };
        }
    }
}
