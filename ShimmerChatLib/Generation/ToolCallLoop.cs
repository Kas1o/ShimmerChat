using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Generation;

/// <summary>
/// 生成轮次循环。纯逻辑，不读写外界状态：
/// 负责流式累积与轮次上限；prompt 构建、工具执行、循环决策全部委托给 <see cref="IToolCallLoopHost"/>。
/// </summary>
public class ToolCallLoop
{
    public async Task RunAsync(
        IChatCompletionClient api,
        IReadOnlyList<Tool> toolDefinitions,
        IToolCallLoopHost host,
        int maxRounds = Int32.MaxValue,
        bool streaming = true,
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

            if (streaming)
            {
                await foreach (var chunk in api.GenerateStreamAsync(pb, ct))
                {
                    Accumulate(chunk, accumulated);
                    await host.OnStreamDeltaAsync(accumulated, ct);
                }
            }
            else
            {
                accumulated = await api.GenerateAsync(pb);
                await host.OnStreamDeltaAsync(accumulated, ct);
            }

            if (!await host.OnAssistantCompleteAsync(accumulated, ct))
                return;
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
