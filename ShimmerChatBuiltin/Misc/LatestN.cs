using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;

namespace ShimmerChatBuiltin.Misc
{
	public class LatestNConfig : ModifierConfig
	{
		[UiHint("保留条数", "要保留的最新消息数量")]
		public int Count { get; set; } = 10;

		public bool KeepFirstSystem { get; set; } = true;
	}

	public class LatestN : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(LatestN),
			Description = "Keep only the latest N messages. Set KeepFirstSystem to preserve the first system message."
		};

		public Type ConfigType => typeof(LatestNConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (LatestNConfig)config;
			var n = cfg.Count;
			var keepFirstSystem = cfg.KeepFirstSystem;

			if (keepFirstSystem)
			{
				var first = context.Segments.FirstOrDefault();
				keepFirstSystem = first != null && first.From == PromptBuilder.From.system;
			}

			if (n <= 0)
			{
				throw new Exception("LatestN Input Err, number should be > 0");
			}
			if (n >= context.Segments.Count)
			{
				return;
			}

			var backup = keepFirstSystem ? context.Segments[0] : null;
			var list = context.Segments.TakeLast(n).ToList();

			if (keepFirstSystem && backup != null)
			{
				list.Insert(0, backup);
			}

			var messagesToKeep = list.ToHashSet();
			var removedToolCallIds = new HashSet<string>();
			foreach (var segment in context.Segments.Except(messagesToKeep))
			{
				if (segment.From == PromptBuilder.From.assistant && segment.Message.toolCalls != null && segment.Message.toolCalls.Count > 0)
				{
					foreach (var toolCall in segment.Message.toolCalls)
					{
						if (!string.IsNullOrEmpty(toolCall.id))
						{
							removedToolCallIds.Add(toolCall.id);
						}
					}
				}
			}

			var finalList = new List<ContextSegment>();
			foreach (var segment in list)
			{
				if (segment.From == PromptBuilder.From.tool_result)
				{
					if (string.IsNullOrEmpty(segment.Message.id) || !removedToolCallIds.Contains(segment.Message.id))
					{
						finalList.Add(segment);
					}
				}
				else
				{
					finalList.Add(segment);
				}
			}

			context.Segments.Clear();
			context.Segments.AddRange(finalList);
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			var cfg = (LatestNConfig)config;
			if (cfg.Count <= 0)
				return (false, "Count must be greater than 0");
			return (true, "");
		}
	}
}
