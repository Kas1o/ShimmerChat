using SharperLLM.FunctionCalling;
using ShimmerChatLib.Tool;

namespace ShimmerChat.Singletons
{
	public interface IToolService
	{
		List<ITool> LoadedTools { get; }
		List<ITool> EnabledTools { get; }

		void EnableTool(string name);
		void DisableTool(string name);

		IEnumerable<Tool> GetEnabledToolDefinitions();

		Task<string?> ExecuteToolAsync(string toolName, string arguments);
	}
}
