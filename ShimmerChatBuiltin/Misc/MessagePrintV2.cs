using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;

namespace ShimmerChatBuiltin.Misc
{
	public class MessagePrintV2Config : ModifierConfig
	{
		public bool ShowTimestamps { get; set; }
		public List<string> SenderFilters { get; set; } = new();
	}

	public class MessagePrintV2 : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "MessagePrintV2",
			Description = "Colorized message dump to console. Filter by sender and toggle timestamp display."
		};

		public Type ConfigType => typeof(MessagePrintV2Config);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var pConfig = (MessagePrintV2Config)config;

			foreach (var segment in context.Segments)
			{
				if (pConfig.SenderFilters.Count > 0)
				{
					var senderStr = segment.From.ToString();
					if (!pConfig.SenderFilters.Contains(senderStr, StringComparer.OrdinalIgnoreCase))
						continue;
				}

				var timestamp = segment.Metadata.TryGetValue("timestamp", out var ts) ? $" [{ts}]" : "";
				var contentPreview = segment.Message?.Content?.Length > 100
					? segment.Message.Content[..100] + "..."
					: segment.Message?.Content ?? "";

				Console.ForegroundColor = segment.From switch
				{
					PromptBuilder.From.user => ConsoleColor.Green,
					PromptBuilder.From.assistant => ConsoleColor.Cyan,
					PromptBuilder.From.system => ConsoleColor.Yellow,
					PromptBuilder.From.tool_result => ConsoleColor.Magenta,
					_ => ConsoleColor.Gray
				};

				Console.WriteLine($"[{segment.From}]{timestamp} {contentPreview}");
				Console.ResetColor();
			}
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			var pConfig = (MessagePrintV2Config)config;
			foreach (var filter in pConfig.SenderFilters)
			{
				var valid = filter is "user" or "assistant" or "system" or "tool_result";
				if (!valid)
					return (false, $"Invalid sender filter: {filter}. Valid: user, assistant, system, tool_result.");
			}
			return (true, "");
		}
	}
}
