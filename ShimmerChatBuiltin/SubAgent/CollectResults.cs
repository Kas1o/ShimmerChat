using System.Text;
using System.Text.RegularExpressions;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;

namespace ShimmerChatBuiltin.SubAgent
{
    public class CollectResultsConfig : ModifierConfig
    {
        [UiHint("输出 ID 列表", "逗号分隔的 BackgroundGeneration 输出 ID")]
        public string OutputIds { get; set; } = "";
    }

    public class CollectResults : IContextModifier
    {
        public ContextModifierInfo info => new()
        {
            Name = "CollectResults",
            Description = "Collect background generation results."
        };

        public Type ConfigType => typeof(CollectResultsConfig);

        public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");

        public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
        {
            var cfg = (CollectResultsConfig)config;
            var ids = cfg.OutputIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0) return;

            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                if (!context.SharedState.TryGetValue(id, out var taskObj))
                    continue;
                context.SharedState.Remove(id);

                string? content;
                try
                {
                    content = ((Task<string>)taskObj).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    content = $"[Error: {ex.Message}]";
                }
                if (content == null) continue;

                var safeTag = SanitizeXmlTag(id);
                sb.Append('<').Append(safeTag).Append('>');
                sb.Append(content);
                sb.Append("</").Append(safeTag).Append('>');
                sb.Append(' ');
            }

            if (sb.Length > 0)
            {
                context.Segments.Add(new ContextSegment
                {
                    SourceType = typeof(CollectResults),
                    Message = new ChatMessage { Content = sb.ToString().TrimEnd() },
                    From = PromptBuilder.From.system
                });
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
