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

        private ContextDocument CreateContextDocument(Chat chat, string system, List<Tool>? tools = null)
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
                        Message = x.message,
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

            var pb = new PromptBuilder();
            if (tools != null)
            {
                pb.AvailableTools = tools;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            return new ContextDocument
            {
                Template = pb,
                Segments = segments
            };
        }

        public ContextDocument BuildContextDocument(Chat chat, Agent agent)
        {
            var context = CreateContextDocument(chat, agent.description);
            _contextModifierService.ApplyModifiers(context, chat, agent);
            return context;
        }

        public ContextDocument BuildContextDocumentWithTools(Chat chat, Agent agent, List<Tool> toolDefinitions)
        {
            var context = CreateContextDocument(chat, agent.description, toolDefinitions);
            _contextModifierService.ApplyModifiers(context, chat, agent);
            return context;
        }

        public PromptBuilder BuildPromptBuilder(Chat chat, Agent agent)
        {
            var context = BuildContextDocument(chat, agent);
            context.RenderTo(context.Template);
            return context.Template;
        }

        public PromptBuilder BuildPromptBuilderWithTools(Chat chat, Agent agent, List<Tool> toolDefinitions)
        {
            var context = BuildContextDocumentWithTools(chat, agent, toolDefinitions);
            context.RenderTo(context.Template);
            return context.Template;
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
            if (toolDefinitions != null)
            {
                pb.AvailableTools = toolDefinitions;
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }

            var context = new ContextDocument
            {
                Template = pb,
                Segments = p.Select(m => new ContextSegment
                {
                    Message = m.Item1,
                    From = m.Item2
                }).ToList()
            };
            _contextModifierService.ApplyModifiers(context, chat, agent);
            context.RenderTo(pb);

            return pb;
        }
    }
}
