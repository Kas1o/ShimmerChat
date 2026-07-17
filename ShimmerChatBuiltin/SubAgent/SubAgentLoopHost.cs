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
    /// 环境重建回调。调用后返回一组新的初始 Fragment，SubAgentLoopHost 会将其与
    /// 循环中累积的对话消息合并构造新的 SubAgentPromptContext。
    /// null = 不重建。
    /// </summary>
    private readonly Func<Task<List<ContextSegment>>>? _rebuildFragments;

    /// <summary>获取当前 PromptContext（重建后可能被替换），供输出格式化使用。</summary>
    public SubAgentPromptContext CurrentContext => (SubAgentPromptContext)_promptContext;

    public SubAgentLoopHost(
        SubAgentPromptContext promptContext,
        IToolExecutor toolExecutor,
        Func<ChatMessage, Task<ChatMessage>>? postProcessor = null,
        Func<Task<List<ContextSegment>>>? rebuildFragments = null)
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
    /// </summary>
    public async Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
    {
        _promptContext.AppendToolResult(toolCall.id ?? "", toolCall.name, result);

        if (_rebuildFragments == null)
            return;

        try
        {
            // 从当前 Context 取出累积的对话消息（SubAgentPromptContext 才有 Messages 属性）
            var current = (SubAgentPromptContext)_promptContext;
            var accumulatedMessages = current.Messages.ToList();

            // 重建：重执行修饰器树，拿到全新初始 Fragment
            var freshFragments = await _rebuildFragments();

            // 合并：新初始 Fragment + 之前累积的对话消息
            var merged = freshFragments
                .Select(s => (s.Message, s.From))
                .Concat(accumulatedMessages)
                .ToList();

            _promptContext = new SubAgentPromptContext(merged);
        }
        catch
        {
            // 重建失败则保留旧状态
        }
    }
}
