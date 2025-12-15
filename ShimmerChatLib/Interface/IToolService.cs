using ShimmerChatLib;
using ShimmerChatLib.Tool;
using SharperLLM.FunctionCalling;

namespace ShimmerChatLib.Interface
{
	public interface IToolService
	{
		List<ITool> LoadedTools { get; }
		List<ITool> EnabledTools { get; }

		void EnableTool(string name);
		void DisableTool(string name);

		IEnumerable<SharperLLM.FunctionCalling.Tool> GetEnabledToolDefinitions();

		Task<string?> ExecuteToolAsync(string toolName, string arguments, Chat chat, Agent agent);
	}
}
