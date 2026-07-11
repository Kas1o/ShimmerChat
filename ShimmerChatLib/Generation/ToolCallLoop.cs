using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation;

/// <summary>
/// Tool Call 循环。纯逻辑，不读写外界状态。
/// 负责流式累积、循环控制、错误处理；prompt 构建和工具执行委托给 IToolCallLoopHost。
/// </summary>
public class ToolCallLoop
{
    public async Task RunAsync(
        ILLMAPI api,
        IReadOnlyList<Tool> toolDefinitions,
        IToolCallLoopHost host,
        int maxRounds = 50,
        bool continueOnToolError = false,
        CancellationToken ct = default)
    {
        for (int round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var pb = host.BuildPromptBuilder(toolDefinitions);

            var accumulated = new ResponseEx
            {
                Body = new ChatMessage { Content = "" },
                FinishReason = FinishReason.None
            };

            await foreach (var chunk in api.GenerateChatExStream(pb, ct))
            {
                Accumulate(chunk, accumulated);
                await host.OnStreamDeltaAsync(accumulated, ct);
            }

            if (!await host.OnAssistantCompleteAsync(accumulated, ct))
                return;

            if (accumulated.FinishReason != FinishReason.FunctionCall
                || accumulated.Body.toolCalls == null
                || accumulated.Body.toolCalls.Count == 0)
                return;

            foreach (var tc in accumulated.Body.toolCalls)
            {
                ct.ThrowIfCancellationRequested();
                string result;
                try
                {
                    result = await host.ExecuteToolAsync(tc, ct);
                }
                catch (Exception ex) when (continueOnToolError)
                {
                    result = $"[Tool error] {ex.Message}";
                }
                await host.OnToolCompleteAsync(tc, result, ct);
            }
        }
    }

    private static void Accumulate(ResponseEx chunk, ResponseEx acc)
    {
        acc.Body.Content += chunk.Body.Content;
        acc.FinishReason = chunk.FinishReason;
        if (chunk.Body.thinking != null)
            acc.Body.thinking = (acc.Body.thinking ?? "") + chunk.Body.thinking;
        if (chunk.Body.id != null)
            acc.Body.id = chunk.Body.id;

        if (chunk.Body.toolCalls != null)
        {
            acc.Body.toolCalls ??= new List<ToolCall>();
            foreach (var tc in chunk.Body.toolCalls)
            {
                var existing = acc.Body.toolCalls.FirstOrDefault(t => t.index == tc.index);
                if (existing != null)
                    existing.arguments = (existing.arguments ?? "") + (tc.arguments ?? "");
                else
                    acc.Body.toolCalls.Add(new ToolCall
                    {
                        name = tc.name,
                        id = tc.id,
                        arguments = tc.arguments ?? "",
                        index = tc.index
                    });
            }
        }
    }
}
