using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using ShimmerChat.Singletons;
using ShimmerChatLib.Generation.ToolUi;

namespace ShimmerChat.Tests;

/// <summary>
/// 交互通道的四路竞态：提交 / 跳过 / 超时 / 取消。只有一路能胜出，
/// 其余必须拿到结构化错误而不是静默失败。
/// </summary>
public class ToolInteractionServiceTests
{
    private readonly Guid _chatGuid = Guid.NewGuid();

    private static ToolInteractionService CreateService()
        => new(NullLogger<ToolInteractionService>.Instance);

    private ToolInteractionRequest Request() => new()
    {
        ToolName = "ask_user",
        Title = "Which one?",
        Form = ToolUiForm.Ask("Which one?", ["A", "B"], allowFreeText: false)
    };

    [Fact]
    public async Task Submit_DeliversAnswerAndClearsPending()
    {
        var service = CreateService();
        var pendingSignal = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PendingChanged += id => pendingSignal.TrySetResult(id);

        var requestTask = service.RequestAsync(_chatGuid, Request(), timeout: null, CancellationToken.None);

        var changedChat = await pendingSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        changedChat.Should().Be(_chatGuid);

        var pending = service.GetPending(_chatGuid);
        pending.Should().ContainSingle();

        var answer = new JObject { ["choice"] = "B" };
        service.Submit(pending[0].Id, answer).Success.Should().BeTrue();

        var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

        result.Outcome.Should().Be(ToolInteractionOutcome.Answered);
        result.Answer!["choice"]!.Value<string>().Should().Be("B");
        service.GetPending(_chatGuid).Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_DeliversSkippedOutcome()
    {
        var service = CreateService();
        var requestTask = service.RequestAsync(_chatGuid, Request(), timeout: null, CancellationToken.None);

        var pending = await WaitForPending(service, _chatGuid);
        service.Skip(pending.Id, "user_skipped").Success.Should().BeTrue();

        var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        result.Outcome.Should().Be(ToolInteractionOutcome.Skipped);
        result.Reason.Should().Be("user_skipped");
        result.ToModelJson().Should().Contain("skipped");
    }

    [Fact]
    public async Task SecondSubmit_ReportsAlreadyResolved()
    {
        var service = CreateService();
        var requestTask = service.RequestAsync(_chatGuid, Request(), timeout: null, CancellationToken.None);

        var pending = await WaitForPending(service, _chatGuid);
        service.Submit(pending.Id, new JObject { ["answer"] = "first" }).Success.Should().BeTrue();
        await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

        var second = service.Submit(pending.Id, new JObject { ["answer"] = "second" });

        // 记录已被移除，因此第二次提交只能得到"已结束"
        second.Success.Should().BeFalse();
        second.Code.Should().BeOneOf(
            ToolInteractionSubmitCodes.AlreadyResolved,
            ToolInteractionSubmitCodes.NotFound);
    }

    [Fact]
    public async Task Timeout_EndsTheWaitWithoutAnAnswer()
    {
        var service = CreateService();
        var result = await service
            .RequestAsync(_chatGuid, Request(), TimeSpan.FromMilliseconds(50), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Outcome.Should().Be(ToolInteractionOutcome.TimedOut);
        result.ToModelJson().Should().Contain("timed_out");
        service.GetPending(_chatGuid).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_ThrowsInsteadOfFakingAnOutcome()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var requestTask = service.RequestAsync(_chatGuid, Request(), timeout: null, cts.Token);
        await WaitForPending(service, _chatGuid);

        cts.Cancel();

        // 取消必须抛 OCE：把它伪装成"用户未响应"会让"停止生成"变成静默继续
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => requestTask.WaitAsync(TimeSpan.FromSeconds(5)));

        service.GetPending(_chatGuid).Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAll_OnlyAffectsTheTargetChat()
    {
        var service = CreateService();
        var otherChat = Guid.NewGuid();

        var first = service.RequestAsync(_chatGuid, Request(), timeout: null, CancellationToken.None);
        var second = service.RequestAsync(otherChat, Request(), timeout: null, CancellationToken.None);

        await WaitForPending(service, _chatGuid);
        await WaitForPending(service, otherChat);

        service.CancelAll(_chatGuid, "generation_stopped").Should().Be(1);

        var cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5));
        cancelled.Outcome.Should().Be(ToolInteractionOutcome.Cancelled);

        service.GetPending(otherChat).Should().ContainSingle();
        service.CancelAll(otherChat, "test_cleanup");
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>等待第一条待回答记录出现（RequestAsync 在后台注册，注册完成前 GetPending 为空）。</summary>
    private static async Task<ToolInteractionPending> WaitForPending(ToolInteractionService service, Guid chatGuid)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var list = service.GetPending(chatGuid);
            if (list.Count > 0) return list[0];

            await Task.Delay(10);
        }

        throw new TimeoutException($"No pending interaction appeared for chat {chatGuid}.");
    }
}
