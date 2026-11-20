using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;
using ShimmerChatLib.Generation;

namespace ShimmerChatLib.Tests;

/// <summary>
/// ProviderData 是服务端私有数据的唯一搬运通道：克隆必须深拷贝（MemberwiseClone 只复制引用），
/// 流式累积必须跨 chunk 合并。丢字段 = 跨轮签名丢失 = Gemini 3/4 工具循环 400。
/// </summary>
public class ProviderDataTests
{
    [Fact]
    public void Clone_DeepCopiesProviderData()
    {
        var original = new ChatMessage
        {
            Content = "x",
            ProviderData = new Dictionary<string, string> { ["gemini.thoughtSignature"] = "SIG" }
        };

        var clone = (ChatMessage)original.Clone();
        clone.ProviderData!["gemini.thoughtSignature"] = "MUTATED";

        // MemberwiseClone 只复制字典引用，不深拷贝就会让克隆体与原件共享可变状态
        original.ProviderData!["gemini.thoughtSignature"].Should().Be("SIG");
    }

    [Fact]
    public void ResponseExMerge_CombinesProviderData_NextWins()
    {
        var prev = new ResponseEx
        {
            Body = new ChatMessage
            {
                Content = "a",
                ProviderData = new Dictionary<string, string> { ["k1"] = "p1", ["k2"] = "p2" }
            },
            FinishReason = FinishReason.None
        };
        var next = new ResponseEx
        {
            Body = new ChatMessage
            {
                Content = "b",
                ProviderData = new Dictionary<string, string> { ["k2"] = "n2", ["k3"] = "n3" }
            },
            FinishReason = FinishReason.Stop
        };

        var combined = prev + next;

        combined.Body.ProviderData.Should().HaveCount(3);
        combined.Body.ProviderData!["k1"].Should().Be("p1");
        combined.Body.ProviderData!["k2"].Should().Be("n2");
        combined.Body.ProviderData!["k3"].Should().Be("n3");
    }

    [Fact]
    public async Task ToolCallLoop_Accumulate_MergesProviderDataFromChunks()
    {
        var api = new ConfigurableChatClient();
        ResponseEx SigChunk(string sig, string text) => new()
        {
            Body = new ChatMessage
            {
                Content = text,
                ProviderData = new Dictionary<string, string>
                {
                    [GeminiChatCompletionClient.ThoughtSignatureKey] = sig
                }
            },
            FinishReason = FinishReason.None
        };
        api.AddStreamChunks(
            SigChunk("SIG_A", "Hel"),
            SigChunk("SIG_B", "lo"),
            new ResponseEx { Body = new ChatMessage { Content = "" }, FinishReason = FinishReason.Stop });

        ResponseEx? final = null;
        var host = new StubToolCallLoopHost(onComplete: (rsp, ct) =>
        {
            final = rsp;
            return Task.FromResult(false);
        });

        await new ToolCallLoop().RunAsync(api, Array.Empty<Tool>(), host);

        final.Should().NotBeNull();
        final!.Body.Content.Should().Be("Hello");
        final.Body.ProviderData![GeminiChatCompletionClient.ThoughtSignatureKey].Should().Be("SIG_B");
    }
}
