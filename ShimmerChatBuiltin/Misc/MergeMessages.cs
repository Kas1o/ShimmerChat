using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class MergeMessagesConfig : ModifierConfig
	{
		public bool AsSystem { get; set; }
		public bool AsUser { get; set; }
		public bool AsAssistant { get; set; }
	}

	public class MergeMessages : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(MergeMessages),
			Description = "Merge All Messages Into One"
		};

		public Type ConfigType => typeof(MergeMessagesConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			if(!(config is MergeMessagesConfig mergeMessagesConfig))
			{
				throw new Exception("MergeMessages Config Error.");
			}

			var sb = new StringBuilder();
			foreach(var seg in context.Segments)
			{
				sb.AppendLine(">>>" + seg.From.ToString() + ":");
				sb.AppendLine(seg.Message.Content);
			}
			context.Segments = [new ContextSegment 
			{
				Message = new ChatMessage{Content = sb.ToString()},
				From = (mergeMessagesConfig.AsAssistant, mergeMessagesConfig.AsUser, mergeMessagesConfig.AsSystem) switch
				{
					(true,false,false) => PromptBuilder.From.assistant,
					(false,true,false) => PromptBuilder.From.user,
					(false,false,true) => PromptBuilder.From.system,
					_ => throw new Exception("MergeMessages Config Error.")
				}
			}];
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			if(config is MergeMessagesConfig mergeMessagesConfig)
			{
				if( (mergeMessagesConfig.AsSystem ? 1 : 0) +
					(mergeMessagesConfig.AsAssistant ? 1 : 0) +
					(mergeMessagesConfig.AsUser ? 1 : 0) != 1
				)
				{
					return (false, "MergeMessageConfig Can only use one role.");
				}
				return (true, "");
			}
			return (false, "");
		}
	}
}
