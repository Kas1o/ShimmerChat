using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Context
{
	public interface IContextModifier
	{
		public ContextModifierInfo info { get; }
		public Type ConfigType { get; }

		public void ModifyContext(ContextDocument context, ModifierConfig config, Chat chat, Agent agent);
		public (bool IsValid, string Error) Validate(ModifierConfig config);
	}

	public struct ContextModifierInfo
	{
		public required string Name { get; set; }
		public required string Description { get; set; }
	}
}
