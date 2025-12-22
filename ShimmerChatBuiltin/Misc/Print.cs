using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class Print : IContextModifier
	{
		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "Print",
			Description = "Prints input to the console. {time} macro supports",
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			Console.WriteLine(input.Replace("{time}", DateTime.Now.ToString("g")));
		}
	}
}
