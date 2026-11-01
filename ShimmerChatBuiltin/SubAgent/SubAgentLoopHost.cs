using SharperLLM.Agents;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.SubAgent;

/// <summary>
/// 一轮后处理在 SubAgent 侧的产物：本轮响应、循环意图，以及节点直接写进虚拟对话的新消息。
/// </summary>
public sealed class SubAgentPostRound
{
    public required ChatMessage ResponseMessage { get; init; }
    public PostLoopDirective LoopDirective { get; init; } = PostLoopDirective.Stop;

    /// <summary>后处理节点追加到虚拟对话的消息（如工具结果），需要并入子代理上下文。</summary>
    public IReadOnlyList<Message> Appended { get; init; } = Array.Empty<Message>();
}

/// <summary>
/// IToolCallLoopHost 适配器，桥接轮次循环和 SubAgent 的 IPromptContext。
/// SubAgent 不需要流式输出，OnStreamDeltaAsync 为空操作。
///
/// 行为对齐主流程 GenerationManagerV2.MainLoopHost：
/// 1. OnAssistantCompleteAsync：走后处理树 → 并入对话增量 → 按意图决定是否重建环境并继续
/// 2. 工具执行不再由宿主负责，而是由后处理树中的工具节点直接写入虚拟对话
///
/// 对话累积策略：
/// - _conversation 列表维护 assistant + 追加消息增量，每次收到即追加
/// - 重建时将 _conversation 传给回调，回调负责 种子消息 + conversation → 写入虚拟 Chat.Messages → 重执行树
/// - 树产出的 Fragment 直接替换 _promptContext，不做任何二次合并
/// </summary>
public class SubAgentLoopHost : IToolCallLoopHost
{
    private IPromptContext _promptContext;
    private readonly Func<ChatMessage, Task<SubAgentPostRound>>? _postProcessor;

    /// <summary>
    /// 累积的对话增量（assistant + 节点追加的消息，不含种子消息和树产出 Fragment）。
    /// </summary>
    private readonly List<(ChatMessage, PromptBuilder.From)> _conversation = new();

    /// <summary>
    /// 环境重建回调。接收当前累积的对话增量，由调用方合并种子消息后写入虚拟 Chat 的 Messages，
    /// 重执行修饰器树，返回全新 Fragment 列表。null = 不重建。
    /// </summary>
    private readonly Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? _rebuildFragments;

    /// <summary>
    /// 增量消息回调：每条消息追加进对话增量后立即回调一次，供调用方落盘。
    /// null = 不落盘。
    /// </summary>
    private readonly Action<(ChatMessage Message, PromptBuilder.From From)>? _onMessageAppended;

    /// <summary>获取当前 PromptContext（重建后可能被替换），供输出格式化使用。</summary>
    public SubAgentPromptContext CurrentContext => (SubAgentPromptContext)_promptContext;

    public SubAgentLoopHost(
        SubAgentPromptContext promptContext,
        Func<ChatMessage, Task<SubAgentPostRound>>? postProcessor = null,
        Func<List<(ChatMessage, PromptBuilder.From)>, Task<List<ContextSegment>>>? rebuildFragments = null,
        Action<(ChatMessage Message, PromptBuilder.From From)>? onMessageAppended = null)
    {
        _promptContext = promptContext;
        _postProcessor = postProcessor;
        _rebuildFragments = rebuildFragments;
        _onMessageAppended = onMessageAppended;
    }

    public PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions)
        => _promptContext.BuildPromptBuilder(toolDefinitions);

    public Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// 每次 assistant 响应完整接收后：走后处理树 → 并入对话增量 → 按循环意图决定是否重建并继续。
    /// </summary>
    public async Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct)
    {
        var body = fullResponse.Body;

        var round = _postProcessor != null
            ? await _postProcessor(body)
            : new SubAgentPostRound { ResponseMessage = body };

        _promptContext.AppendAssistantMessage(round.ResponseMessage);
        var assistant = (SubAgentPromptContext.CloneChatMessage(round.ResponseMessage), PromptBuilder.From.assistant);
        _conversation.Add(assistant);
        _onMessageAppended?.Invoke(assistant);

        foreach (var msg in round.Appended)
        {
            var from = SenderToFrom(msg.sender);
            var content = msg.CurrentVersion ?? new ChatMessage { Content = "" };

            // 没有重建回调时（例如后处理管线缺失），直接追加到当前上下文；
            // 有重建时这些内容会由 conversation 重新构建，重复追加无害。
            AppendToContext(content, from);

            var appended = (SubAgentPromptContext.CloneChatMessage(content), from);
            _conversation.Add(appended);
            _onMessageAppended?.Invoke(appended);
        }

        if (round.LoopDirective != PostLoopDirective.Continue)
            return false;

        await RebuildAsync();
        return true;
    }

    private void AppendToContext(ChatMessage message, PromptBuilder.From from)
    {
        switch (from)
        {
            case PromptBuilder.From.assistant:
                _promptContext.AppendAssistantMessage(message);
                break;
            case PromptBuilder.From.tool_result:
                _promptContext.AppendToolResult(message.id ?? "", "", message.Content);
                break;
            default:
                // user / system 没有对应的追加 API，重建时会从 conversation 进入上下文
                break;
        }
    }

    /// <summary>
    /// 用累积的对话增量重建上下文（重执行子代理修饰器树）。
    /// 失败时保留旧状态并把原因写进调试输出，不静默吞掉。
    /// </summary>
    private async Task RebuildAsync()
    {
        if (_rebuildFragments == null)
            return;

        var freshFragments = await _rebuildFragments(_conversation.ToList());
        _promptContext = new SubAgentPromptContext(freshFragments.Select(s => (s.Message, s.From)));
    }

    private static PromptBuilder.From SenderToFrom(string sender) => sender switch
    {
        Sender.User => PromptBuilder.From.user,
        Sender.AI => PromptBuilder.From.assistant,
        Sender.ToolResult => PromptBuilder.From.tool_result,
        _ => PromptBuilder.From.system
    };
}
