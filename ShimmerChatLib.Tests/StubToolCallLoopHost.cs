using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// IToolCallLoopHost 测试桩
/// </summary>
public class StubToolCallLoopHost : IToolCallLoopHost
{
    private readonly Func<IReadOnlyList<Tool>, PromptBuilder>? _buildPrompt;
    private readonly Func<ToolCall, CancellationToken, Task<string>>? _executeTool;
    private readonly Func<ResponseEx, CancellationToken, Task>? _onDelta;
    private readonly Func<ResponseEx, CancellationToken, Task<bool>>? _onComplete;
    private readonly Func<ToolCall, string, CancellationToken, Task>? _onToolComplete;

    public StubToolCallLoopHost(
        Func<IReadOnlyList<Tool>, PromptBuilder>? buildPrompt = null,
        Func<ToolCall, CancellationToken, Task<string>>? executeTool = null,
        Func<ResponseEx, CancellationToken, Task>? onDelta = null,
        Func<ResponseEx, CancellationToken, Task<bool>>? onComplete = null,
        Func<ToolCall, string, CancellationToken, Task>? onToolComplete = null)
    {
        _buildPrompt = buildPrompt;
        _executeTool = executeTool;
        _onDelta = onDelta;
        _onComplete = onComplete;
        _onToolComplete = onToolComplete;
    }

    public List<int> DeltaCallOrder { get; } = new();
    public int RoundCount { get; private set; }

    public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        => _buildPrompt?.Invoke(toolDefinitions) ?? new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

    public Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
        => _executeTool?.Invoke(toolCall, ct) ?? Task.FromResult("ok");

    public async Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
    {
        DeltaCallOrder.Add(RoundCount);
        if (_onDelta != null)
            await _onDelta(accumulated, ct);
    }

    public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
    {
        RoundCount++;
        if (_onComplete != null)
            return await _onComplete(fullResponse, ct);
        return true;
    }

    public Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
        => _onToolComplete?.Invoke(toolCall, result, ct) ?? Task.CompletedTask;
}
