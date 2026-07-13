using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 可配置的伪 ChatCompletionClient，用于生成管线测试。
/// 支持预设响应序列、流式分块模拟、FunctionCall 模拟。
/// </summary>
public class ConfigurableChatClient : IChatCompletionClient
{
    private readonly Queue<ResponseEx> _responses = new();
    private readonly Queue<ResponseEx[]> _streamChunks = new();
    private ResponseEx? _defaultResponse;

    public int CallCount { get; private set; }

    /// <summary>设置预设响应序列，每次 GenerateStreamAsync/GenerateAsync 依次消费</summary>
    public void AddResponse(ResponseEx response) => _responses.Enqueue(response);

    /// <summary>设置预设流式分块，每个响应对应一组 chunk</summary>
    public void AddStreamChunks(params ResponseEx[] chunks) => _streamChunks.Enqueue(chunks);

    /// <summary>当预设序列耗尽时使用的兜底响应</summary>
    public void SetDefaultResponse(ResponseEx response) => _defaultResponse = response;

    /// <summary>快捷方法：创建一个 Stop 响应</summary>
    public static ResponseEx CreateStopResponse(string content)
        => new()
        {
            Body = new ChatMessage { Content = content },
            FinishReason = FinishReason.Stop
        };

    /// <summary>快捷方法：创建一个 FunctionCall 响应</summary>
    public static ResponseEx CreateToolCallResponse(params ToolCall[] toolCalls)
        => new()
        {
            Body = new ChatMessage
            {
                Content = "",
                toolCalls = toolCalls.ToList()
            },
            FinishReason = FinishReason.FunctionCall
        };

    /// <summary>快捷方法：创建分块 (流式 delta)</summary>
    public static ResponseEx CreateChunk(string content, FinishReason reason = FinishReason.None)
        => new()
        {
            Body = new ChatMessage { Content = content },
            FinishReason = reason
        };

    public Task<ResponseEx> GenerateAsync(PromptBuilder pb)
    {
        CallCount++;
        var response = ConsumeResponse();
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ResponseEx> GenerateStreamAsync(
        PromptBuilder pb,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CallCount++;

        // 优先取流式分块
        if (_streamChunks.TryDequeue(out var chunks))
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
            yield break;
        }

        // 否则取单个响应 (作为单 chunk 流式返回)
        var response = ConsumeResponse();
        yield return response;
    }

    private ResponseEx ConsumeResponse()
    {
        if (_responses.TryDequeue(out var response))
            return response;

        return _defaultResponse ?? CreateStopResponse("");
    }
}
