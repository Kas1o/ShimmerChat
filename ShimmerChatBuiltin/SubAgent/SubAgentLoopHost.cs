using SharperLLM.Agents;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent;

/// <summary>
/// IToolCallLoopHost 适配器，桥接 ToolCallLoop 和 SubAgent 的 IPromptContext + IToolExecutor。
/// SubAgent 不需要流式输出，OnStreamDeltaAsync 为空操作。
/// </summary>
public class SubAgentLoopHost : IToolCallLoopHost
{
    private readonly IPromptContext _promptContext;
    private readonly IToolExecutor _toolExecutor;

    public SubAgentLoopHost(IPromptContext promptContext, IToolExecutor toolExecutor)
    {
        _promptContext = promptContext;
        _toolExecutor = toolExecutor;
    }

    public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        => _promptContext.BuildPromptBuilder(toolDefinitions);

    public Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
        => _toolExecutor.ExecuteAsync(toolCall, ct)!;

    public Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
        => Task.CompletedTask;

    public Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
    {
        _promptContext.AppendAssistantMessage(fullResponse.Body);
        return Task.FromResult(true);
    }

    public Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
    {
        _promptContext.AppendToolResult(toolCall.id ?? "", toolCall.name, result);
        return Task.CompletedTask;
    }
}
