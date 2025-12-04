using SharperLLM.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatLib.Context
{
	public interface IContextModifier
	{
		public ContextModifierInfo info { get; }
		public void ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent);
	}

	public struct ContextModifierInfo
	{
		public required string Name { get; set; }
		public required string Description { get; set; }
	}
}
