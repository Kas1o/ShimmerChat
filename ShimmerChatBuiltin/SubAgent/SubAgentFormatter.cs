using Newtonsoft.Json;
using SharperLLM.Util;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// SubAgent 输出格式化：LastMessage / FullJson / None。
    /// </summary>
    public static class SubAgentFormatter
    {
        /// <summary>
        /// 根据 OutputMode 格式化消息列表。
        /// </summary>
        public static string Format(string outputMode, SubAgentPromptContext ctx)
        {
            switch (outputMode)
            {
                case "FullJson":
                    return SerializeInteraction(ctx.Messages);
                case "None":
                    return "";
                case "LastMessage":
                default:
                    return ctx.LastAssistantContent ?? "";
            }
        }

        private static string SerializeInteraction(
            IReadOnlyList<(ChatMessage Message, PromptBuilder.From From)> messages)
        {
            var items = messages.Select(m => new
            {
                role = RoleToString(m.From),
                content = m.Message.Content ?? "",
                tool_calls = m.Message.toolCalls?.Select(tc => new
                {
                    name = tc.name,
                    arguments = tc.arguments
                }).ToList()
            }).ToList();

            return JsonConvert.SerializeObject(items, Formatting.Indented);
        }

        private static string RoleToString(PromptBuilder.From from)
        {
            if (from == PromptBuilder.From.user) return "user";
            if (from == PromptBuilder.From.assistant) return "assistant";
            if (from == PromptBuilder.From.system) return "system";
            if (from == PromptBuilder.From.tool_result) return "tool_result";
            return "unknown";
        }
    }
}
