using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class STStyleMacro : IContextModifier
	{
		IKVDataService KVData;

		public STStyleMacro(IKVDataService kvData)
		{
			KVData = kvData;
		}

		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "ST Style Macro",
			Description = "Adds support for SillyTavern style macros in prompts.",
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			var username = KVData.Read("User", "username") ?? "User";
			var agentName = agent.name;

			foreach (var item in promptBuilder.Messages)
			{
				item.Item1.Content = item.Item1.Content.Replace("{{user}}", username);
				item.Item1.Content = item.Item1.Content.Replace("{{char}}", agentName);
			}
		}
	}
}
