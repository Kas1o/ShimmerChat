using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class MessagePrintLatestConfig : ModifierConfig
	{

	}

	public class MessagePrintLatest : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "MessagePrintLatest",
			Description = "Print the latest message content"
		};

		public Type ConfigType => typeof(MessagePrintLatestConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			Console.WriteLine(context.Segments.Last()?.Message.Content);
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config)
		{
			return (true, null);
		}
	}
}
