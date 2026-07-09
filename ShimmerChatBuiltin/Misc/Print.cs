using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Misc
{
	public class PrintConfig : ModifierConfig
	{
		public string Template { get; set; } = "";

		public override string ToString()
		{
			return string.IsNullOrEmpty(Template) ? "(empty)"
				: Template.Length > 40 ? Template[..37] + "..."
				: Template;
		}
	}

	public class Print : IContextModifier
	{
		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "Print",
			Description = "Prints input to the console. {time} and {total_len} macros supported."
		};

		public Type ConfigType => typeof(PrintConfig);

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var cfg = (PrintConfig)config;
			Console.WriteLine(cfg.Template
				.Replace("{time}", DateTime.Now.ToString("g"))
				.Replace("{total_len}", context.Segments.Select(s => s.Message.Content?.Length ?? 0).Sum().ToString()));
		}

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");
	}
}
