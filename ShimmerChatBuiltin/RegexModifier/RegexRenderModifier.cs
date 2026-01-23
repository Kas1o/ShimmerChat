using ShimmerChatLib;
using ShimmerChatLib.Interface;
using System;
using System.Text.RegularExpressions;

namespace ShimmerChatBuiltin.RegexModifier
{
    public class RegexRenderModifier : IMessageRenderModifier
    {
        private readonly IKVDataService _kvData;

        public RegexRenderModifier(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public MessageRenderModifierInfo Info => new MessageRenderModifierInfo
        {
            Name = "Regex Replacement",
            Description = "Applies a set of Regex replacement rules to the message content. Input: Name of the Regex Set to apply."
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