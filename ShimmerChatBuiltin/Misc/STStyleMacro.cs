using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using ShimmerChatLib.Interface;
using System.Text.RegularExpressions;

namespace ShimmerChatBuiltin.Misc
{
	public class STStyleMacroConfig : ModifierConfig
	{
	}

	public class STStyleMacro : IContextModifier
	{
		private readonly IKVDataService _kvData;

		public STStyleMacro(IKVDataService kvData)
		{
			_kvData = kvData;
		}

		public ContextModifierInfo info => new ContextModifierInfo
		{
			Name = "ST Style Macro",
			Description = "Replace {{user}} and {{char}} macros in messages."
		};

		public Type ConfigType => typeof(STStyleMacroConfig);

		public (bool IsValid, string Error) Validate(ModifierConfig config) => (true, "");

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent)
		{
			var username = _kvData.Read("User", "username") ?? "User";
			var charname = agent.Name ?? agent.Guid.ToString();

			foreach (var segment in context.Segments)
			{
				segment.Message.Content = Regex.Replace(segment.Message.Content, @"\{\{user\}\}", username, RegexOptions.IgnoreCase);
				segment.Message.Content = Regex.Replace(segment.Message.Content, @"\{\{char\}\}", charname, RegexOptions.IgnoreCase);
			}
		}
	}
}
