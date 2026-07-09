using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Generation;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class MessagePrintV2Config : ModifierConfig
	{
		public bool Colorize { get; set; } = true;
		public bool IgnoreNullProperties { get; set; } = true;
		public bool ColorizeToolArgs { get; set; } = true;
		public bool Sixel { get; set; }
		public bool Kitty { get; set; }

		public override string ToString()
		{
			var flags = new List<string>();
			if (Colorize) flags.Add("color");
			if (IgnoreNullProperties) flags.Add("no-null");
			if (ColorizeToolArgs) flags.Add("color-args");
			return flags.Count > 0 ? string.Join(", ", flags) : "plain";
		}
	}

	public class MessagePrintV2 : IContextModifier
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

		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "MessagePrintV2",
			Description = "Print messages with readable format and role-based ANSI coloring."
		};

		public Type ConfigType => typeof(MessagePrintV2Config);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (MessagePrintV2Config)config;

			if (cfg.Sixel)
				Console.WriteLine($"{WarningColor}[Warning] sixel support is not yet implemented.{Reset}");
			if (cfg.Kitty)
				Console.WriteLine($"{WarningColor}[Warning] kitty graphics support is not yet implemented.{Reset}");

			var sb = new StringBuilder();
			var messages = context.Segments.Select(s => (s.Message, s.From)).ToArray();

			AppendHeader(sb, "Messages Dump", cfg.Colorize);
			AppendSeparator(sb, cfg.Colorize);

			if (messages.Length == 0)
			{
				AppendLine(sb, "No messages found.", cfg.Colorize ? "\x1b[90m" : null, cfg.Colorize);
			}
			else
			{
				for (int i = 0; i < messages.Length; i++)
				{
					var (chatMessage, from) = messages[i];
					AppendMessage(sb, i, chatMessage, from, cfg);

					if (i < messages.Length - 1)
					{
						AppendSeparator(sb, cfg.Colorize);
					}
				}
			}

			AppendSeparator(sb, cfg.Colorize);
			AppendHeader(sb, $"Total: {messages.Length} messages", cfg.Colorize);

			Console.WriteLine(sb.ToString());
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");

		private void AppendMessage(StringBuilder sb, int index, ChatMessage message, PromptBuilder.From from, MessagePrintV2Config cfg)
		{
			var roleColor = GetRoleColor(from, cfg.Colorize);
			var roleName = GetRoleName(from);

			AppendLine(sb, $"[{index}] {roleName}", cfg.Colorize ? $"{HeaderColor}{roleColor}" : null, cfg.Colorize);

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(message.id))
			{
				var displayValue = message.id ?? "(null)";
				AppendLabeledValue(sb, "  ID", displayValue, cfg.Colorize ? LabelColor : null, cfg.Colorize ? ContentColor : null, cfg.Colorize);
			}

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(message.Content))
			{
				var displayValue = message.Content ?? "(null)";
				AppendLabeledValue(sb, "  Content", displayValue, cfg.Colorize ? LabelColor : null, cfg.Colorize ? ContentColor : null, cfg.Colorize);
			}

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(message.ImageBase64))
			{
				string displayValue;
				if (string.IsNullOrEmpty(message.ImageBase64))
				{
					displayValue = cfg.IgnoreNullProperties ? "" : "(null)";
				}
				else
				{
					displayValue = $"[Image: {message.ImageBase64.Length} chars]";
				}

				if (!string.IsNullOrEmpty(displayValue))
				{
					AppendLabeledValue(sb, "  Image", displayValue, cfg.Colorize ? LabelColor : null, cfg.Colorize ? "\x1b[95m" : null, cfg.Colorize);
				}
			}

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(message.thinking))
			{
				var displayValue = message.thinking ?? "(null)";
				AppendLabeledValue(sb, "  Thinking", displayValue, cfg.Colorize ? LabelColor : null, cfg.Colorize ? ThinkingColor : null, cfg.Colorize);
			}

			if (!cfg.IgnoreNullProperties || (message.toolCalls != null && message.toolCalls.Count > 0))
			{
				if (message.toolCalls == null || message.toolCalls.Count == 0)
				{
					if (!cfg.IgnoreNullProperties)
						AppendLine(sb, "  ToolCalls: (null)", cfg.Colorize ? LabelColor : null, cfg.Colorize);
				}
				else
				{
					AppendLine(sb, "  ToolCalls:", cfg.Colorize ? LabelColor : null, cfg.Colorize);
					foreach (var toolCall in message.toolCalls)
					{
						AppendToolCall(sb, toolCall, cfg);
					}
				}
			}

			if (!cfg.IgnoreNullProperties || (message.CustomProperties != null && message.CustomProperties.Count > 0))
			{
				if (message.CustomProperties == null || message.CustomProperties.Count == 0)
				{
					if (!cfg.IgnoreNullProperties)
						AppendLine(sb, "  CustomProperties: (null)", cfg.Colorize ? LabelColor : null, cfg.Colorize);
				}
				else
				{
					AppendLine(sb, "  CustomProperties:", cfg.Colorize ? LabelColor : null, cfg.Colorize);
					foreach (var prop in message.CustomProperties)
					{
						var valueStr = prop.Value?.ToString() ?? "null";
						AppendLabeledValue(sb, $"    {prop.Key}", valueStr, cfg.Colorize ? LabelColor : null, cfg.Colorize ? "\x1b[37m" : null, cfg.Colorize);
					}
				}
			}
		}

		private void AppendToolCall(StringBuilder sb, SharperLLM.API.ToolCall toolCall, MessagePrintV2Config cfg)
		{
			var indent = "    ";
			AppendLine(sb, $"{indent}[{toolCall.index}] {toolCall.name}", cfg.Colorize ? ToolCallColor : null, cfg.Colorize);

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(toolCall.id))
			{
				var displayValue = toolCall.id ?? "(null)";
				AppendLabeledValue(sb, $"{indent}  ID", displayValue, cfg.Colorize ? LabelColor : null, cfg.Colorize ? ContentColor : null, cfg.Colorize);
			}

			if (!cfg.IgnoreNullProperties || !string.IsNullOrEmpty(toolCall.arguments))
			{
				if (string.IsNullOrEmpty(toolCall.arguments))
				{
					if (!cfg.IgnoreNullProperties)
						AppendLabeledValue(sb, $"{indent}  Arguments", "(null)", cfg.Colorize ? LabelColor : null, cfg.Colorize ? ContentColor : null, cfg.Colorize);
				}
				else
				{
					AppendLine(sb, $"{indent}  Arguments:", cfg.Colorize ? LabelColor : null, cfg.Colorize);

					if (cfg.Colorize && cfg.ColorizeToolArgs)
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
							AppendFallbackArguments(sb, toolCall.arguments, indent, cfg.Colorize);
						}
					}
					else
					{
						AppendFallbackArguments(sb, toolCall.arguments, indent, cfg.Colorize);
					}
				}
			}
		}

		private void AppendFallbackArguments(StringBuilder sb, string arguments, string indent, bool colorize)
		{
			try
			{
				var parsed = JsonConvert.DeserializeObject(arguments);
				var formatted = JsonConvert.SerializeObject(parsed, Formatting.Indented);
				var lines = formatted.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var line in lines)
				{
					AppendLine(sb, $"{indent}    {line}", colorize ? "\x1b[37m" : null, colorize);
				}
			}
			catch
			{
				AppendLabeledValue(sb, $"{indent}  Arguments", arguments, colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
			}
		}

		private static string GetRoleColor(PromptBuilder.From from, bool colorize)
		{
			if (!colorize) return "";
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
