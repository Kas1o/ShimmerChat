using SharperLLM.Agents;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent;

/// <summary>
/// IToolCallLoopHost 适配器，桥接 ToolCallLoop 和 SubAgent 的 IPromptContext + IToolExecutor。
/// SubAgent 不需要流式输出，OnStreamDeltaAsync 为空操作。
///
/// 行为对齐主流程 GenerationManagerV2.MainLoopHost：
/// 1. OnAssistantCompleteAsync：始终走后处理器（post-processor），再追加到上下文
/// 2. OnToolCompleteAsync：追加工具结果 + 重建环境（重执行修饰器树）
/// </summary>
public class SubAgentLoopHost : IToolCallLoopHost
{
    private IPromptContext _promptContext;
    private readonly IToolExecutor _toolExecutor;
    private readonly Func<ChatMessage, Task<ChatMessage>>? _postProcessor;

    /// <summary>
    /// 环境重建回调。接收当前累积的全部对话消息（初始片段 + assistant + tool_result），
    /// 由调用方注入 SharedState 后重执行修饰器树，返回全新 Fragment 列表。
    /// 返回的 Fragment 即作为新的 SubAgentPromptContext 内容，无需额外合并。
    /// null = 不重建。
    /// </summary>
    private readonly Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? _rebuildFragments;

    /// <summary>获取当前 PromptContext（重建后可能被替换），供输出格式化使用。</summary>
    public SubAgentPromptContext CurrentContext => (SubAgentPromptContext)_promptContext;

    public SubAgentLoopHost(
        SubAgentPromptContext promptContext,
        IToolExecutor toolExecutor,
        Func<ChatMessage, Task<ChatMessage>>? postProcessor = null,
        Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? rebuildFragments = null)
    {
        _promptContext = promptContext;
        _toolExecutor = toolExecutor;
        _postProcessor = postProcessor;
        _rebuildFragments = rebuildFragments;
    }

    public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        => _promptContext.BuildPromptBuilder(toolDefinitions);

    public Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
        => _toolExecutor.ExecuteAsync(toolCall, ct)!;

    public Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// 每次 assistant 响应完整接收后：走后处理器 → 追加到上下文。
    /// 对齐主流程：每轮（含 FunctionCall 轮次）都走后生成管线。
    /// </summary>
    public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
    {
        var body = fullResponse.Body;
        if (_postProcessor != null)
        {
            try { body = await _postProcessor(body); }
            catch { /* 失败时保留原始消息 */ }
        }
        _promptContext.AppendAssistantMessage(body);
        return true;
    }

    /// <summary>
    /// 每次工具执行完成后：追加工具结果 → 重建环境。
    /// 对齐主流程：每轮工具调用后重建 env 让修饰器树重新执行。
    /// 将当前全部累积消息传入 rebuild 回调，由回调注入 SharedState 后重执行树，
    /// 树产出的 Fragment 直接作为新的 PromptContext（无需额外合并，避免初始消息重复）。
    /// </summary>
    public async Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
    {
        _promptContext.AppendToolResult(toolCall.id ?? "", toolCall.name, result);

        if (_rebuildFragments == null)
            return;

        try
        {
            var current = (SubAgentPromptContext)_promptContext;
            var allMessages = current.Messages.ToList();

            // 重建：将全部累积消息传给回调，由回调注入 SharedState 后重执行树
            var freshFragments = await _rebuildFragments(allMessages);

            // 树已产出完整上下文，直接替换（不再拼接，消除重复）
            _promptContext = new SubAgentPromptContext(
                freshFragments.Select(s => (s.Message, s.From)));
        }
        catch
        {
            // 重建失败则保留旧状态
        }
    }
}
