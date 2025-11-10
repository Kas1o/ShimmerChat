using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	public static class Utils
	{
		public static PromptBuilder ToPromptBuilder(this Chat chat, string system, List<Tool> tools = null)
		{
			var pb = new PromptBuilder();
			var p = chat.Messages.Select(
				x =>
				 x.sender.ToLower() switch
				{
					"user" => (x.message, PromptBuilder.From.user),
					"system" => (x.message, PromptBuilder.From.system),
					"ai" => (x.message, PromptBuilder.From.assistant),
					"tool_call" => ((x.message as ToolCallChatMessage), PromptBuilder.From.tool_call),
					"tool_result" => ((x.message as ToolChatMessage), PromptBuilder.From.tool_result)
				}
			).ToList();
			p.Insert(0, (system, PromptBuilder.From.system));
			pb.Messages = p.ToArray();
			if(tools != null)
			{
				pb.AvailableTools = tools;
				pb.AvailableToolsFormatter = ToolPromptParser.Parse;
			}
			return pb;
		}
	}
}
