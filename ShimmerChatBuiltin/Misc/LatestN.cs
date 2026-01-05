using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.Misc
{
	public class LatestN : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(LatestN),
			Description = "Latest N messages"
		};

		public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			var n = int.Parse(input);
			if(n <= 0)
			{
				throw new Exception("LatestN Input Err, number should be > 0");
			}

			promptBuilder.Messages =  promptBuilder.Messages.TakeLast(n).ToArray();
		}
	}
}
