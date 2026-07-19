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
///
/// 对话累积策略：
/// - _conversation 列表直接维护 assistant + tool_result 增量，每次消息到达即追加
/// - 重建时将 _conversation 传给回调，回调负责 种子消息 + conversation → SharedState → 重执行树
/// - 树产出的 Fragment 直接替换 _promptContext，不做任何二次合并
/// </summary>
public class SubAgentLoopHost : IToolCallLoopHost
{
    private IPromptContext _promptContext;
    private readonly IToolExecutor _toolExecutor;
    private readonly Func<ChatMessage, Task<ChatMessage>>? _postProcessor;

    /// <summary>
    /// 累积的对话增量（仅 assistant + tool_result，不含种子消息和树产出 Fragment）。
    /// 每次 OnAssistantCompleteAsync / OnToolCompleteAsync 直接追加。
    /// </summary>
    private readonly List<(ChatMessage, PromptBuilder.From)> _conversation = new();

    /// <summary>
    /// 环境重建回调。接收当前累积的对话增量（assistant + tool_result），
    /// 由调用方合并种子消息后注入 SharedState，重执行修饰器树，返回全新 Fragment 列表。
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
    /// 每次 assistant 响应完整接收后：走后处理器 → 追加到上下文和对话增量列表。
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
        _conversation.Add((SubAgentPromptContext.CloneChatMessage(body), PromptBuilder.From.assistant));
        return true;
    }

    /// <summary>
    /// 每次工具执行完成后：追加工具结果到上下文和对话增量列表 → 重建环境。
    /// </summary>
    public async Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct)
    {
        _promptContext.AppendToolResult(toolCall.id ?? "", toolCall.name, result);
        _conversation.Add((new ChatMessage { Content = result, id = toolCall.id ?? "" }, PromptBuilder.From.tool_result));

        if (_rebuildFragments == null)
            return;

        try
        {
            // 将对话增量传给回调，由回调合并种子消息后注入 SharedState 重执行树
            var freshFragments = await _rebuildFragments(_conversation.ToList());

            // 树产出即完整上下文，直接替换
            _promptContext = new SubAgentPromptContext(
                freshFragments.Select(s => (s.Message, s.From)));
        }
        catch
        {
            // 重建失败则保留旧状态
        }
    }
}
