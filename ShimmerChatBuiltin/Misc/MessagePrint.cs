using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SharperLLM.Util;
using ShimmerChatBuiltin.Misc;
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
			Description = "print message dump to console. input true/false to colorize the output. default: true",
		};

		void IContextModifier.ModifyContext(PromptBuilder promptBuilder, string input, Chat chat, Agent agent)
		{
			var colorize = true;
			if (bool.TryParse(input, out var output))
			{
				colorize = output;
			}

			var json = JsonConvert.SerializeObject(promptBuilder.Messages, Settings);
			if(colorize)
				JsonColorizer.WriteToConsole(json);
			else
				Console.WriteLine(json);
		}
	}
}
