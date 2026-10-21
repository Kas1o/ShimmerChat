using SharperLLM.Util;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.SubAgent.Sessions
{
    /// <summary>
    /// 会话消息与管线消息之间的映射。
    /// 会话内部复用 <see cref="Message"/> 存储（sender / timestamp / 多版本齐全），
    /// 只有落盘时才需要把 <see cref="ChatMessage"/> + <see cref="PromptBuilder.From"/> 包成 Message。
    /// </summary>
    public static class SubAgentSessionMessages
    {
        /// <summary>把一次管线消息包装成可持久化的 <see cref="Message"/>（深拷贝，避免与管线状态共享引用）。</summary>
        public static Message FromPrompt(ChatMessage message, PromptBuilder.From from, DateTime? timestamp = null)
        {
            return new Message
            {
                message = SubAgentPromptContext.CloneChatMessage(message),
                sender = ToSender(from),
                timestamp = timestamp ?? DateTime.Now
            };
        }

        public static Message FromPrompt((ChatMessage Message, PromptBuilder.From From) appended, DateTime? timestamp = null)
            => FromPrompt(appended.Message, appended.From, timestamp);

        /// <summary>sender 字符串 → 管线角色。</summary>
        public static PromptBuilder.From FromSender(string sender) => sender switch
        {
            Sender.User => PromptBuilder.From.user,
            Sender.AI => PromptBuilder.From.assistant,
            Sender.ToolResult => PromptBuilder.From.tool_result,
            Sender.System => PromptBuilder.From.system,
            _ => PromptBuilder.From.system
        };

        /// <summary>管线角色 → sender 字符串。</summary>
        public static string ToSender(PromptBuilder.From from) => from switch
        {
            PromptBuilder.From.user => Sender.User,
            PromptBuilder.From.assistant => Sender.AI,
            PromptBuilder.From.tool_result => Sender.ToolResult,
            PromptBuilder.From.system => Sender.System,
            _ => Sender.System
        };
    }
}
