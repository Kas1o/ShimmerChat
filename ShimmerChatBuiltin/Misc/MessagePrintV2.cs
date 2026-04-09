using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.Misc;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class MessagePrintV2 : IContextModifier
	{
		// ANSI 颜色定义
		private const string Reset = "\x1b[0m";
		private const string HeaderColor = "\x1b[1;36m"; // 亮青色 - 消息头
		private const string UserColor = "\x1b[1;32m"; // 亮绿色 - User
		private const string AssistantColor = "\x1b[1;34m"; // 亮蓝色 - Assistant
		private const string SystemColor = "\x1b[1;35m"; // 亮紫色 - System
		private const string ToolResultColor = "\x1b[1;33m"; // 亮黄色 - ToolResult
		private const string LabelColor = "\x1b[90m"; // 灰色 - 标签
		private const string ContentColor = "\x1b[97m"; // 白色 - 内容
		private const string ThinkingColor = "\x1b[33m"; // 黄色 - thinking
		private const string ToolCallColor = "\x1b[96m"; // 青色 - tool calls
		private const string SeparatorColor = "\x1b[90m"; // 灰色 - 分隔线
		private const string WarningColor = "\x1b[1;31m"; // 亮红色 - 警告

		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "MessagePrintV2",
			Description = "print messages with readable format and role-based coloring. Usage: colorize=true/false, ignore_null_properties=true/false, colorize_tool_args=true/false, sixel=true/false, kitty=true/false",
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			// 解析参数
			var options = ParseOptions(input);
			var colorize = options.GetValueOrDefault("colorize", true);
			var ignoreNullProperties = options.GetValueOrDefault("ignore_null_properties", true);
			var colorizeToolArgs = options.GetValueOrDefault("colorize_tool_args", true);
			var sixel = options.GetValueOrDefault("sixel", false);
			var kitty = options.GetValueOrDefault("kitty", false);

			// 检查未实现的参数
			if (sixel)
			{
				Console.WriteLine($"{WarningColor}[Warning] sixel support is not yet implemented.{Reset}");
			}
			if (kitty)
			{
				Console.WriteLine($"{WarningColor}[Warning] kitty graphics support is not yet implemented.{Reset}");
			}

			var sb = new StringBuilder();
			var messages = promptBuilder.Messages;

			// 打印标题
			AppendHeader(sb, "Messages Dump", colorize);
			AppendSeparator(sb, colorize);

			if (messages == null || messages.Length == 0)
			{
				AppendLine(sb, "No messages found.", colorize ? "\x1b[90m" : null, colorize);
			}
			else
			{
				for (int i = 0; i < messages.Length; i++)
				{
					var (chatMessage, from) = messages[i];
					AppendMessage(sb, i, chatMessage, from, colorize, ignoreNullProperties, colorizeToolArgs);
					
					// 消息之间添加分隔线（最后一个除外）
					if (i < messages.Length - 1)
					{
						AppendSeparator(sb, colorize);
					}
				}
			}

			AppendSeparator(sb, colorize);
			AppendHeader(sb, $"Total: {messages?.Length ?? 0} messages", colorize);

			Console.WriteLine(sb.ToString());
		}

		private Dictionary<string, bool> ParseOptions(string input)
		{
			var options = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["colorize"] = true,
				["ignore_null_properties"] = true,
				["colorize_tool_args"] = true,
				["sixel"] = false,
				["kitty"] = false
			};

			if (string.IsNullOrWhiteSpace(input))
			{
				return options;
			}

			// 支持格式: key=value,key2=value2 或 key=value;key2=value2
			var pairs = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			
			foreach (var pair in pairs)
			{
				var parts = pair.Split('=', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2)
				{
					var key = parts[0].Trim();
					var value = parts[1].Trim();
					
					if (bool.TryParse(value, out var boolValue))
					{
						options[key] = boolValue;
					}
				}
			}

			return options;
		}

		private void AppendMessage(StringBuilder sb, int index, ChatMessage message, PromptBuilder.From from, bool colorize, bool ignoreNullProperties, bool colorizeToolArgs)
		{
			var roleColor = GetRoleColor(from, colorize);
			var roleName = GetRoleName(from);

			// 消息序号和角色
			AppendLine(sb, $"[{index}] {roleName}", colorize ? $"{HeaderColor}{roleColor}" : null, colorize);

			// ID
			if (!ignoreNullProperties || !string.IsNullOrEmpty(message.id))
			{
				var displayValue = message.id ?? "(null)";
				AppendLabeledValue(sb, "  ID", displayValue, colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
			}

			// Content (主要内容)
			if (!ignoreNullProperties || !string.IsNullOrEmpty(message.Content))
			{
				var displayValue = message.Content ?? "(null)";
				AppendLabeledValue(sb, "  Content", displayValue, colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
			}

			// ImageBase64
			if (!ignoreNullProperties || !string.IsNullOrEmpty(message.ImageBase64))
			{
				string displayValue;
				if (string.IsNullOrEmpty(message.ImageBase64))
				{
					displayValue = ignoreNullProperties ? "" : "(null)";
				}
				else
				{
					displayValue = $"[Image: {message.ImageBase64.Length} chars]";
				}
				
				if (!string.IsNullOrEmpty(displayValue))
				{
					AppendLabeledValue(sb, "  Image", displayValue, colorize ? LabelColor : null, colorize ? "\x1b[95m" : null, colorize);
				}
			}

			// Thinking
			if (!ignoreNullProperties || !string.IsNullOrEmpty(message.thinking))
			{
				var displayValue = message.thinking ?? "(null)";
				AppendLabeledValue(sb, "  Thinking", displayValue, colorize ? LabelColor : null, colorize ? ThinkingColor : null, colorize);
			}

			// ToolCalls
			if (!ignoreNullProperties || (message.toolCalls != null && message.toolCalls.Count > 0))
			{
				if (message.toolCalls == null || message.toolCalls.Count == 0)
				{
					if (!ignoreNullProperties)
					{
						AppendLine(sb, "  ToolCalls: (null)", colorize ? LabelColor : null, colorize);
					}
				}
				else
				{
					AppendLine(sb, "  ToolCalls:", colorize ? LabelColor : null, colorize);
					foreach (var toolCall in message.toolCalls)
					{
						AppendToolCall(sb, toolCall, colorize, ignoreNullProperties, colorizeToolArgs);
					}
				}
			}

			// CustomProperties
			if (!ignoreNullProperties || (message.CustomProperties != null && message.CustomProperties.Count > 0))
			{
				if (message.CustomProperties == null || message.CustomProperties.Count == 0)
				{
					if (!ignoreNullProperties)
					{
						AppendLine(sb, "  CustomProperties: (null)", colorize ? LabelColor : null, colorize);
					}
				}
				else
				{
					AppendLine(sb, "  CustomProperties:", colorize ? LabelColor : null, colorize);
					foreach (var prop in message.CustomProperties)
					{
						var valueStr = prop.Value?.ToString() ?? "null";
						AppendLabeledValue(sb, $"    {prop.Key}", valueStr, colorize ? LabelColor : null, colorize ? "\x1b[37m" : null, colorize);
					}
				}
			}
		}

		private void AppendToolCall(StringBuilder sb, SharperLLM.API.ToolCall toolCall, bool colorize, bool ignoreNullProperties, bool colorizeToolArgs)
		{
			var indent = "    ";
			AppendLine(sb, $"{indent}[{toolCall.index}] {toolCall.name}", colorize ? ToolCallColor : null, colorize);
			
			// ID
			if (!ignoreNullProperties || !string.IsNullOrEmpty(toolCall.id))
			{
				var displayValue = toolCall.id ?? "(null)";
				AppendLabeledValue(sb, $"{indent}  ID", displayValue, colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
			}
			
			// Arguments
			if (!ignoreNullProperties || !string.IsNullOrEmpty(toolCall.arguments))
			{
				if (string.IsNullOrEmpty(toolCall.arguments))
				{
					if (!ignoreNullProperties)
					{
						AppendLabeledValue(sb, $"{indent}  Arguments", "(null)", colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
					}
				}
				else
				{
					AppendLine(sb, $"{indent}  Arguments:", colorize ? LabelColor : null, colorize);
					
					// 尝试使用 JsonColorizer 进行彩色格式化
					if (colorize && colorizeToolArgs)
					{
						try
						{
							// 使用 indentLevel=0，让 MessagePrintV2 自己控制缩进
							var colorizedJson = JsonColorizer.Colorize(toolCall.arguments, 0);
							// 将彩色 JSON 的每一行添加适当的缩进
							var lines = colorizedJson.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
							foreach (var line in lines)
							{
								sb.AppendLine($"{indent}    {line}");
							}
						}
						catch
						{
							// JsonColorizer 失败，fallback 到普通显示
							AppendFallbackArguments(sb, toolCall.arguments, indent, colorize);
						}
					}
					else
					{
						// 不使用彩色格式化，使用普通显示
						AppendFallbackArguments(sb, toolCall.arguments, indent, colorize);
					}
				}
			}
		}

		private void AppendFallbackArguments(StringBuilder sb, string arguments, string indent, bool colorize)
		{
			// 尝试格式化 JSON 参数
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
				// 如果解析失败，直接显示原始字符串
				AppendLabeledValue(sb, $"{indent}  Arguments", arguments, colorize ? LabelColor : null, colorize ? ContentColor : null, colorize);
			}
		}

		private string GetRoleColor(PromptBuilder.From from, bool colorize)
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

		private string GetRoleName(PromptBuilder.From from)
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
			{
				sb.AppendLine($"{HeaderColor}═══ {text} ═══{Reset}");
			}
			else
			{
				sb.AppendLine($"═══ {text} ═══");
			}
		}

		private void AppendSeparator(StringBuilder sb, bool colorize)
		{
			if (colorize)
			{
				sb.AppendLine($"{SeparatorColor}────────────────────────────────────────{Reset}");
			}
			else
			{
				sb.AppendLine("────────────────────────────────────────");
			}
		}

		private void AppendLine(StringBuilder sb, string text, string? color, bool colorize)
		{
			if (colorize && color != null)
			{
				sb.AppendLine($"{color}{text}{Reset}");
			}
			else
			{
				sb.AppendLine(text);
			}
		}

		private void AppendLabeledValue(StringBuilder sb, string label, string value, string? labelColor, string? valueColor, bool colorize)
		{
			// 处理多行内容
			var lines = value.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
			
			if (lines.Length == 0)
			{
				if (colorize && labelColor != null)
				{
					sb.AppendLine($"{labelColor}{label}:{Reset}");
				}
				else
				{
					sb.AppendLine($"{label}:");
				}
			}
			else if (lines.Length == 1)
			{
				// 单行内容
				if (colorize && labelColor != null && valueColor != null)
				{
					sb.AppendLine($"{labelColor}{label}:{Reset} {valueColor}{lines[0]}{Reset}");
				}
				else
				{
					sb.AppendLine($"{label}: {lines[0]}");
				}
			}
			else
			{
				// 多行内容
				if (colorize && labelColor != null)
				{
					sb.AppendLine($"{labelColor}{label}:{Reset}");
				}
				else
				{
					sb.AppendLine($"{label}:");
				}
				
				foreach (var line in lines)
				{
					if (colorize && valueColor != null)
					{
						sb.AppendLine($"    {valueColor}{line}{Reset}");
					}
					else
					{
						sb.AppendLine($"    {line}");
					}
				}
			}
		}
	}
}
