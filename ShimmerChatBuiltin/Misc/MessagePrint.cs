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
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = nameof(MessagePrint),
			Description = "Print the messages to the console."
		};

		public Type ConfigType => typeof(MessagePrintConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(context.Segments, Newtonsoft.Json.Formatting.Indented));
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");
	}
}
