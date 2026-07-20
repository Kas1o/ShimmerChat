using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation;

/// <summary>
/// ToolCallLoop 宿主接口。
/// 服务只负责循环逻辑，不读写外界状态；宿主实现负责 prompt 构建、工具执行、结果处理。
/// </summary>
public interface IToolCallLoopHost
{
    /// <summary>构建当前轮的 PromptBuilder</summary>
    PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions);

    /// <summary>执行工具调用</summary>
    Task<string> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct);

    /// <summary>每收到一个流式 delta 时回调</summary>
    Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct);

    /// <summary>assistant 消息完整接收后回调。返回 false 可提前终止循环。</summary>
    Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct);

    /// <summary>工具执行完成后回调</summary>
    Task OnToolCompleteAsync(ToolCall toolCall, string result, CancellationToken ct);
}
