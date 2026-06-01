using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class PrintConfig : ModifierConfig
	{
	}

	public class Print : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(Print),
			Description = "Print the context to the console for debugging"
		};

		public Type ConfigType => typeof(PrintConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			Console.WriteLine(context.Template.GenerateCleanPrompt());
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");
	}
}
