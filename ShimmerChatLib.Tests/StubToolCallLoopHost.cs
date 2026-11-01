using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// IToolCallLoopHost 测试桩。循环本身只负责流式累积与轮次上限，
/// 所以这里只需要能控制"本轮之后是否继续"。
/// </summary>
public class StubToolCallLoopHost : IToolCallLoopHost
{
    private readonly Func<IReadOnlyList<Tool>, PromptBuilder>? _buildPrompt;
    private readonly Func<ResponseEx, CancellationToken, Task>? _onDelta;
    private readonly Func<ResponseEx, CancellationToken, Task<bool>>? _onComplete;

    public StubToolCallLoopHost(
        Func<IReadOnlyList<Tool>, PromptBuilder>? buildPrompt = null,
        Func<ResponseEx, CancellationToken, Task>? onDelta = null,
        Func<ResponseEx, CancellationToken, Task<bool>>? onComplete = null)
    {
        _buildPrompt = buildPrompt;
        _onDelta = onDelta;
        _onComplete = onComplete;
    }

    public List<int> DeltaCallOrder { get; } = new();

    /// <summary>已完成（回调过）的轮数。</summary>
    public int RoundCount { get; private set; }

    /// <summary>每轮完成后宿主给出的继续/结束决定，供断言使用。</summary>
    public List<bool> Decisions { get; } = new();

    public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        => _buildPrompt?.Invoke(toolDefinitions) ?? new PromptBuilder { Messages = Array.Empty<(ChatMessage, PromptBuilder.From)>() };

    public async Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
    {
        DeltaCallOrder.Add(RoundCount);
        if (_onDelta != null)
            await _onDelta(accumulated, ct);
    }

    public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
    {
        RoundCount++;
        var decision = _onComplete != null
            ? await _onComplete(fullResponse, ct)
            : false;
        Decisions.Add(decision);
        return decision;
    }
}
