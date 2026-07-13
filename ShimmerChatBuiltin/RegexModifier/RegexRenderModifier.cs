using ShimmerChatLib;
using ShimmerChatLib.Interface;
using System;
using System.Text.RegularExpressions;

namespace ShimmerChatBuiltin.RegexModifier
{
    public class RegexRenderModifier : IMessageRenderModifier
    {
        private readonly IKVDataService _kvData;
        private readonly ILocService _loc;

        public RegexRenderModifier(IKVDataService kvData, ILocService loc)
        {
            _kvData = kvData;
            _loc = loc;
        }

        public MessageRenderModifierInfo Info => new MessageRenderModifierInfo
        {
            Name = _loc["builtin.regex_render_modifier.name"],
            Description = _loc["builtin.regex_render_modifier.desc"]
        };

        public string Modify(string content, string input, Chat? chat, Agent? agent)
        {
            if (string.IsNullOrWhiteSpace(input))
                return content;

            var regexSet = RegexManager.GetRegexSet(_kvData, input.Trim());
            if (regexSet == null || regexSet.Rules == null)
                return content;

            var result = content;
            foreach (var rule in regexSet.Rules)
            {
                if (rule.IsEnabled && !string.IsNullOrEmpty(rule.Pattern))
                {
                    try
                    {
                        result = Regex.Replace(result, rule.Pattern, rule.Replacement ?? "", rule.Options);
                    }
                    catch (Exception ex)
                    {
                        // In case of invalid regex, ignore this rule
                        Console.WriteLine($"[RegexRenderModifier] Invalid regex pattern '{rule.Pattern}': {ex.Message}");
                    }
                }
            }

            return result;
        }
    }
}