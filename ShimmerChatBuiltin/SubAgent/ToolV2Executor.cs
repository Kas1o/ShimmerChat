using SharperLLM.Agents;
using SharperLLM.API;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent
{
    /// <summary>
    /// IToolExecutor 适配器：将 ToolCallLoopRunner 的 IToolExecutor 桥接到 ShimmerChat 2.0 的 IToolV2 列表。
    /// </summary>
    public class ToolV2Executor : IToolExecutor
    {
        private readonly List<IToolV2> _tools;

        public ToolV2Executor(List<IToolV2> tools)
        {
            _tools = tools;
        }

        public async Task<string?> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
        {
            var tool = _tools.FirstOrDefault(t => t.GetDefinition().name == toolCall.name);
            if (tool == null)
                return $"Tool '{toolCall.name}' not found.";
            return await tool.ExecuteAsync(toolCall.arguments ?? "{}");
        }
    }
}
