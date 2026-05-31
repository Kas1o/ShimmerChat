using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;

namespace ShimmerChatBuiltin.SubAgent
{
    public class CollectResults : IContextModifier
    {
        public ContextModifierInfo info => new()
        {
            Name = "CollectResults",
            Description = "Collect background generation results. Input: comma-separated output IDs."
        };

        public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
        {
            var ids = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0) return;

            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                var content = SubAgentResultStore.Take(id);
                if (content == null) continue;

                var safeTag = SanitizeXmlTag(id);
                sb.Append('<').Append(safeTag).Append('>');
                sb.Append(content);
                sb.Append("</").Append(safeTag).Append('>');
                sb.Append(' ');
            }

            if (sb.Length > 0)
            {
                var parentMessages = promptBuilder.Messages.ToList();
                parentMessages.Add((new ChatMessage { Content = sb.ToString().TrimEnd() }, PromptBuilder.From.system));
                promptBuilder.Messages = parentMessages.ToArray();
            }
        }

        private static string SanitizeXmlTag(string id)
        {
            var sanitized = Regex.Replace(id, @"[^a-zA-Z0-9_.-]", "_");
            if (sanitized.Length == 0 || char.IsDigit(sanitized[0]) || sanitized[0] == '.' || sanitized[0] == '-')
                sanitized = "_" + sanitized;
            return sanitized;
        }
    }
}
