using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;

namespace ShimmerChatBuiltin.Misc
{
	public class MessagePrintConfig : ModifierConfig
	{
	}

	public class MessagePrint : IContextModifier
	{
		private readonly JsonSerializerSettings Settings;

		public MessagePrint()
		{
			Settings = new JsonSerializerSettings();
			Settings.Converters.Add(new StringEnumConverter());
		}

		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "MessagePrint",
			Description = "Print message dump to console. Input will be ignored."
		};

		public Type ConfigType => typeof(MessagePrintConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var messages = context.Segments.Select(s => (s.Message, s.From)).ToArray();
			var json = JsonConvert.SerializeObject(messages, Settings);
			Console.WriteLine(json);
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");
	}
}
