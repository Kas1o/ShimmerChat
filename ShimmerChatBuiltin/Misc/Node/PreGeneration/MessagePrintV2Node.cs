using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.Misc;
using ShimmerChatLib.Generation;
using System.Text;

namespace ShimmerChatBuiltin.Misc.Node.PreGeneration
{
    [NodeInfo("node.message_print_v2", Icon = "🌈", Color = "var(--node-debug)", CategoryKeys = ["category.debug"], DescriptionKey = "node.message_print_v2.desc")]
    public class MessagePrintV2Node : IPreGenerationNode
    {
        private const string Reset = "\x1b[0m";
        private const string HeaderColor = "\x1b[1;36m";
        private const string UserColor = "\x1b[1;32m";
        private const string AssistantColor = "\x1b[1;34m";
        private const string SystemColor = "\x1b[1;35m";
        private const string ToolResultColor = "\x1b[1;33m";
        private const string LabelColor = "\x1b[90m";
        private const string ContentColor = "\x1b[97m";
        private const string ThinkingColor = "\x1b[33m";
        private const string ToolCallColor = "\x1b[96m";
        private const string SeparatorColor = "\x1b[90m";
        private const string WarningColor = "\x1b[1;31m";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Message Print V2";

        [NodeProperty("prop.message_print_v2.colorize", HintKey = "prop.message_print_v2.colorize.hint")]
        public bool Colorize { get; set; } = true;

        [NodeProperty("prop.message_print_v2.ignore_null_properties", HintKey = "prop.message_print_v2.ignore_null_properties.hint")]
        public bool IgnoreNullProperties { get; set; } = true;

        [NodeProperty("prop.message_print_v2.colorize_tool_args", HintKey = "prop.message_print_v2.colorize_tool_args.hint")]
        public bool ColorizeToolArgs { get; set; } = true;

        [NodeProperty("prop.message_print_v2.sixel", HintKey = "prop.message_print_v2.sixel.hint")]
        public bool Sixel { get; set; }

        [NodeProperty("prop.message_print_v2.kitty", HintKey = "prop.message_print_v2.kitty.hint")]
        public bool Kitty { get; set; }

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var output = context.Env.Persistent.DebugOutput;
            var source = nameof(MessagePrintV2Node);

            if (Sixel)
                output.Write(source, "warning", $"{WarningColor}[Warning] sixel support is not yet implemented.{Reset}");
            if (Kitty)
                output.Write(source, "warning", $"{WarningColor}[Warning] kitty graphics support is not yet implemented.{Reset}");

            var sb = new StringBuilder();
            var messages = context.Env.Transient.Fragments
                .Select(s => (s.Message, s.From))
                .ToArray();

            AppendHeader(sb, "Messages Dump", Colorize);
            AppendSeparator(sb, Colorize);

            if (messages.Length == 0)
            {
                AppendLine(sb, "No messages found.", Colorize ? "\x1b[90m" : null, Colorize);
            }
            else
            {
                for (int i = 0; i < messages.Length; i++)
                {
                    var (chatMessage, from) = messages[i];
                    AppendMessage(sb, i, chatMessage, from);

                    if (i < messages.Length - 1)
                        AppendSeparator(sb, Colorize);
                }
            }

            AppendSeparator(sb, Colorize);
            AppendHeader(sb, $"Total: {messages.Length} messages", Colorize);

            output.Write(source, "info", sb.ToString());
            return Task.FromResult(NodeResult.SuccessResult());
        }

        private void AppendMessage(StringBuilder sb, int index, ChatMessage message, PromptBuilder.From from)
        {
            var roleColor = GetRoleColor(from);
            var roleName = GetRoleName(from);

            AppendLine(sb, $"[{index}] {roleName}", Colorize ? $"{HeaderColor}{roleColor}" : null, Colorize);

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(message.id))
            {
                var displayValue = message.id ?? "(null)";
                AppendLabeledValue(sb, "  ID", displayValue, Colorize ? LabelColor : null, Colorize ? ContentColor : null, Colorize);
            }

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(message.Content))
            {
                var displayValue = message.Content ?? "(null)";
                AppendLabeledValue(sb, "  Content", displayValue, Colorize ? LabelColor : null, Colorize ? ContentColor : null, Colorize);
            }

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(message.ImageBase64))
            {
                string displayValue;
                if (string.IsNullOrEmpty(message.ImageBase64))
                {
                    displayValue = IgnoreNullProperties ? "" : "(null)";
                }
                else
                {
                    displayValue = $"[Image: {message.ImageBase64.Length} chars]";
                }

                if (!string.IsNullOrEmpty(displayValue))
                {
                    AppendLabeledValue(sb, "  Image", displayValue, Colorize ? LabelColor : null, Colorize ? "\x1b[95m" : null, Colorize);
                }
            }

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(message.thinking))
            {
                var displayValue = message.thinking ?? "(null)";
                AppendLabeledValue(sb, "  Thinking", displayValue, Colorize ? LabelColor : null, Colorize ? ThinkingColor : null, Colorize);
            }

            if (!IgnoreNullProperties || (message.toolCalls != null && message.toolCalls.Count > 0))
            {
                if (message.toolCalls == null || message.toolCalls.Count == 0)
                {
                    if (!IgnoreNullProperties)
                        AppendLine(sb, "  ToolCalls: (null)", Colorize ? LabelColor : null, Colorize);
                }
                else
                {
                    AppendLine(sb, "  ToolCalls:", Colorize ? LabelColor : null, Colorize);
                    foreach (var toolCall in message.toolCalls)
                    {
                        AppendToolCall(sb, toolCall);
                    }
                }
            }

            if (!IgnoreNullProperties || (message.CustomProperties != null && message.CustomProperties.Count > 0))
            {
                if (message.CustomProperties == null || message.CustomProperties.Count == 0)
                {
                    if (!IgnoreNullProperties)
                        AppendLine(sb, "  CustomProperties: (null)", Colorize ? LabelColor : null, Colorize);
                }
                else
                {
                    AppendLine(sb, "  CustomProperties:", Colorize ? LabelColor : null, Colorize);
                    foreach (var prop in message.CustomProperties)
                    {
                        var valueStr = prop.Value?.ToString() ?? "null";
                        AppendLabeledValue(sb, $"    {prop.Key}", valueStr, Colorize ? LabelColor : null, Colorize ? "\x1b[37m" : null, Colorize);
                    }
                }
            }
        }

        private void AppendToolCall(StringBuilder sb, SharperLLM.API.ToolCall toolCall)
        {
            var indent = "    ";
            AppendLine(sb, $"{indent}[{toolCall.index}] {toolCall.name}", Colorize ? ToolCallColor : null, Colorize);

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(toolCall.id))
            {
                var displayValue = toolCall.id ?? "(null)";
                AppendLabeledValue(sb, $"{indent}  ID", displayValue, Colorize ? LabelColor : null, Colorize ? ContentColor : null, Colorize);
            }

            if (!IgnoreNullProperties || !string.IsNullOrEmpty(toolCall.arguments))
            {
                if (string.IsNullOrEmpty(toolCall.arguments))
                {
                    if (!IgnoreNullProperties)
                        AppendLabeledValue(sb, $"{indent}  Arguments", "(null)", Colorize ? LabelColor : null, Colorize ? ContentColor : null, Colorize);
                }
                else
                {
                    AppendLine(sb, $"{indent}  Arguments:", Colorize ? LabelColor : null, Colorize);

                    if (Colorize && ColorizeToolArgs)
                    {
                        try
                        {
                            var colorizedJson = JsonColorizer.Colorize(toolCall.arguments, 0);
                            var lines = colorizedJson.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                sb.AppendLine($"{indent}    {line}");
                            }
                        }
                        catch
                        {
                            AppendFallbackArguments(sb, toolCall.arguments, indent);
                        }
                    }
                    else
                    {
                        AppendFallbackArguments(sb, toolCall.arguments, indent);
                    }
                }
            }
        }

        private void AppendFallbackArguments(StringBuilder sb, string arguments, string indent)
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject(arguments);
                var formatted = JsonConvert.SerializeObject(parsed, Formatting.Indented);
                var lines = formatted.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    AppendLine(sb, $"{indent}    {line}", Colorize ? "\x1b[37m" : null, Colorize);
                }
            }
            catch
            {
                AppendLabeledValue(sb, $"{indent}  Arguments", arguments, Colorize ? LabelColor : null, Colorize ? ContentColor : null, Colorize);
            }
        }

        private string GetRoleColor(PromptBuilder.From from)
        {
            if (!Colorize) return "";
            return from switch
            {
                PromptBuilder.From.user => UserColor,
                PromptBuilder.From.assistant => AssistantColor,
                PromptBuilder.From.system => SystemColor,
                PromptBuilder.From.tool_result => ToolResultColor,
                _ => ContentColor
            };
        }

        private static string GetRoleName(PromptBuilder.From from)
        {
            return from switch
            {
                PromptBuilder.From.user => "User",
                PromptBuilder.From.assistant => "Assistant",
                PromptBuilder.From.system => "System",
                PromptBuilder.From.tool_result => "ToolResult",
                _ => from.ToString()
            };
        }

        private void AppendHeader(StringBuilder sb, string text, bool colorize)
        {
            if (colorize)
                sb.AppendLine($"{HeaderColor}═══ {text} ═══{Reset}");
            else
                sb.AppendLine($"═══ {text} ═══");
        }

        private void AppendSeparator(StringBuilder sb, bool colorize)
        {
            if (colorize)
                sb.AppendLine($"{SeparatorColor}────────────────────────────────────────{Reset}");
            else
                sb.AppendLine("────────────────────────────────────────");
        }

        private void AppendLine(StringBuilder sb, string text, string? color, bool colorize)
        {
            if (colorize && color != null)
                sb.AppendLine($"{color}{text}{Reset}");
            else
                sb.AppendLine(text);
        }

        private void AppendLabeledValue(StringBuilder sb, string label, string value, string? labelColor, string? valueColor, bool colorize)
        {
            var lines = value.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
            {
                if (colorize && labelColor != null)
                    sb.AppendLine($"{labelColor}{label}:{Reset}");
                else
                    sb.AppendLine($"{label}:");
            }
            else if (lines.Length == 1)
            {
                if (colorize && labelColor != null && valueColor != null)
                    sb.AppendLine($"{labelColor}{label}:{Reset} {valueColor}{lines[0]}{Reset}");
                else
                    sb.AppendLine($"{label}: {lines[0]}");
            }
            else
            {
                if (colorize && labelColor != null)
                    sb.AppendLine($"{labelColor}{label}:{Reset}");
                else
                    sb.AppendLine($"{label}:");

                foreach (var line in lines)
                {
                    if (colorize && valueColor != null)
                        sb.AppendLine($"    {valueColor}{line}{Reset}");
                    else
                        sb.AppendLine($"    {line}");
                }
            }
        }
    }
}
