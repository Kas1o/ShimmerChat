using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.ContextModifiers
{
	public class MessagePrint : IContextModifier
	{
		private JsonSerializerSettings Settings;
		public MessagePrint()
		{
			Settings = new JsonSerializerSettings();
			Settings.Converters.Add(new StringEnumConverter());
		}

		ContextModifierInfo IContextModifier.info => new ContextModifierInfo
		{
			Name = "MessagePrint",
			Description = "print message dump to console. input will be ignored.",
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			var json = JsonConvert.SerializeObject(promptBuilder.Messages, Settings);
			Console.WriteLine(json);
		}
	}
}
