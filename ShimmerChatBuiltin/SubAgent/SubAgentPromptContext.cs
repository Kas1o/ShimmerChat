using SharperLLM.Agents;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IPromptContext 实现，包装消息列表。
    /// 暴露 Messages 用于 SubAgent 输出格式化（FullJson / LastMessage / None）。
    /// </summary>
    public class SubAgentPromptContext : IPromptContext
    {
        private readonly List<(ChatMessage Message, PromptBuilder.From From)> _messages;

        public SubAgentPromptContext(IEnumerable<(ChatMessage Message, PromptBuilder.From From)> initialMessages)
        {
            _messages = initialMessages
                .Select(m => (CloneChatMessage(m.Message), m.From))
                .ToList();
        }

        public SubAgentPromptContext(string userTask)
        {
            _messages = new()
            {
                (new ChatMessage { Content = userTask }, PromptBuilder.From.user)
            };
        }

        /// <summary>只读消息列表，供输出格式化使用。</summary>
        public IReadOnlyList<(ChatMessage Message, PromptBuilder.From From)> Messages => _messages;

        /// <summary>最后一条 assistant 消息的 Content。</summary>
        public string? LastAssistantContent =>
            _messages.LastOrDefault(m => m.From == PromptBuilder.From.assistant).Message?.Content;

        /// <summary>
        /// 替换最后一条 assistant 消息的 Content。
        /// 用于 Post-Generation 管线处理后的文本回写。
        /// </summary>
        public void UpdateLastAssistantContent(string newContent)
        {
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].From == PromptBuilder.From.assistant)
                {
                    _messages[i].Message.Content = newContent;
                    return;
                }
            }
        }

        PromptBuilder IPromptContext.BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        {
            var pb = new PromptBuilder { Messages = _messages.ToArray() };
            if (toolDefinitions.Count > 0)
            {
                pb.AvailableTools = toolDefinitions.ToList();
                pb.AvailableToolsFormatter = ToolPromptParser.Parse;
            }
            return pb;
        }

        void IPromptContext.AppendAssistantMessage(ChatMessage assistantMessage)
        {
            _messages.Add((CloneChatMessage(assistantMessage), PromptBuilder.From.assistant));
        }

        void IPromptContext.AppendToolResult(string toolCallId, string toolName, string toolResult)
        {
            _messages.Add((new ChatMessage { Content = toolResult, id = toolCallId }, PromptBuilder.From.tool_result));
        }

        private static ChatMessage CloneChatMessage(ChatMessage original)
        {
            return new ChatMessage
            {
                Content = original.Content,
                ImageBase64 = original.ImageBase64,
                thinking = original.thinking,
                id = original.id,
                toolCalls = original.toolCalls?.Select(tc => new ToolCall
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
