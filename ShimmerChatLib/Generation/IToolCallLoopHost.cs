using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 生成轮次循环宿主接口。
/// 循环本身只负责"流式累积 + 轮次上限"，本轮之后是否继续、要不要执行工具调用
/// 全部由宿主（后处理管线）决定。
/// </summary>
public interface IToolCallLoopHost
{
    /// <summary>构建当前轮的 PromptBuilder</summary>
    PromptBuilder BuildPromptBuilder(IReadOnlyList<Tool> toolDefinitions);

    /// <summary>每收到一个流式 delta 时回调</summary>
    Task OnStreamDeltaAsync(ResponseEx accumulated, CancellationToken ct);

    /// <summary>
    /// assistant 消息完整接收后回调。宿主在此执行后处理管线、提交消息并重建环境。
    /// 返回 true 表示再生成一轮。
    /// </summary>
    Task<bool> OnAssistantCompleteAsync(ResponseEx fullResponse, CancellationToken ct);
}
