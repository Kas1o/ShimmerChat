using ShimmerChatBuiltin.SubAgent.Sessions;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// 会话列表面板的纯逻辑：关键词 / 状态筛选、排序、分组上限外的事实来源，以及
/// 自动刷新用的「详情是否需要重读」与「消息窗口」计算。
/// </summary>
public class SubAgentSessionQueryTests
{
    private static SubAgentSessionSummary Summary(
        string title,
        Guid? configGuid = null,
        string configName = "probe",
        SubAgentSessionStatus status = SubAgentSessionStatus.Completed,
        int messageCount = 1,
        string? preview = null,
        DateTime? updatedAt = null,
        DateTime? createdAt = null,
        Guid? id = null)
    {
        var guid = id ?? Guid.NewGuid();
        var updated = updatedAt ?? new DateTime(2026, 1, 1, 12, 0, 0);
        return new SubAgentSessionSummary
        {
            Id = guid,
            ConfigGuid = configGuid ?? Guid.Empty,
            ConfigName = configName,
            Title = title,
            Status = status,
            MessageCount = messageCount,
            LastPreview = preview,
            UpdatedAt = updated,
            CreatedAt = createdAt ?? updated.AddMinutes(-5)
        };
    }

    [Fact]
    public void Matches_EmptyKeyword_AndNullStatus_AcceptsEverything()
    {
        var summary = Summary("整理资料");

        SubAgentSessionQuery.Matches(summary, null, null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, "   ", null).Should().BeTrue();
    }

    [Fact]
    public void Matches_IsCaseInsensitive_AndSearchesPreviewConfigAndId()
    {
        var id = Guid.NewGuid();
        var summary = Summary("Weekly Report", preview: "SQLite migration notes", configName: "Analyst", id: id);

        // 标题、预览、配置名、两种 Id 形态都应命中
        SubAgentSessionQuery.Matches(summary, "weekly", null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, "sqlite", null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, "analyst", null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, id.ToString("N")[..8], null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, id.ToString("D"), null).Should().BeTrue();
    }

    [Fact]
    public void Matches_MultipleTokensRequireAllToHit()
    {
        var summary = Summary("整理资料", preview: "output about tokens", configName: "probe");

        SubAgentSessionQuery.Matches(summary, "整理 probe", null).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, "整理 missing", null).Should().BeFalse();
    }

    [Fact]
    public void Matches_StatusFilterIsExclusive()
    {
        var summary = Summary("failed run", status: SubAgentSessionStatus.Failed);

        SubAgentSessionQuery.Matches(summary, null, SubAgentSessionStatus.Failed).Should().BeTrue();
        SubAgentSessionQuery.Matches(summary, null, SubAgentSessionStatus.Completed).Should().BeFalse();
        SubAgentSessionQuery.Matches(summary, "failed", SubAgentSessionStatus.Completed).Should().BeFalse();
    }

    [Fact]
    public void BuildGroups_KeepsOnlyMatchingGroups_ButReportsFullGroupCount()
    {
        var configA = Guid.NewGuid();
        var configB = Guid.NewGuid();

        var summaries = new List<SubAgentSessionSummary>
        {
            Summary("alpha one", configA, "Alpha", updatedAt: new DateTime(2026, 1, 3)),
            Summary("alpha two", configA, "Alpha", updatedAt: new DateTime(2026, 1, 2)),
            Summary("beta one", configB, "Beta", updatedAt: new DateTime(2026, 1, 1))
        };

        var groups = SubAgentSessionQuery.BuildGroups(
            summaries, "alpha one", null, SubAgentSessionSort.UpdatedDesc);

        groups.Should().ContainSingle();
        groups[0].ConfigName.Should().Be("Alpha");
        groups[0].TotalCount.Should().Be(2);
        groups[0].Sessions.Should().ContainSingle().Which.Title.Should().Be("alpha one");
    }

    [Fact]
    public void BuildGroups_SortsGroupsByName_AndSessionsByRequestedOrder()
    {
        var configB = Guid.NewGuid();
        var configA = Guid.NewGuid();

        var summaries = new List<SubAgentSessionSummary>
        {
            Summary("zulu", configB, "Zeta", updatedAt: new DateTime(2026, 1, 1)),
            Summary("bravo", configA, "Alpha", updatedAt: new DateTime(2026, 1, 2), messageCount: 2),
            Summary("charlie", configA, "Alpha", updatedAt: new DateTime(2026, 1, 3), messageCount: 9)
        };

        var updated = SubAgentSessionQuery.BuildGroups(
            summaries, null, null, SubAgentSessionSort.UpdatedDesc);

        updated.Select(g => g.ConfigName).Should().Equal("Alpha", "Zeta");
        updated[0].Sessions.Select(s => s.Title).Should().Equal("charlie", "bravo");

        var byMessages = SubAgentSessionQuery.BuildGroups(
            summaries, null, null, SubAgentSessionSort.MessageCountDesc);

        byMessages[0].Sessions.Select(s => s.Title).Should().Equal("charlie", "bravo");
        byMessages[0].Sessions.Select(s => s.MessageCount).Should().BeInDescendingOrder();
    }

    [Fact]
    public void BuildGroups_ConfigRename_UsesLatestNonEmptyName()
    {
        var config = Guid.NewGuid();
        var summaries = new List<SubAgentSessionSummary>
        {
            Summary("old", config, "Old Name", updatedAt: new DateTime(2026, 1, 3)),
            Summary("new", config, "New Name", updatedAt: new DateTime(2026, 1, 2))
        };

        var groups = SubAgentSessionQuery.BuildGroups(
            summaries, null, null, SubAgentSessionSort.UpdatedDesc);

        groups.Should().ContainSingle();
        groups[0].ConfigName.Should().Be("Old Name");
        groups[0].TotalCount.Should().Be(2);
    }

    [Fact]
    public void CountByStatus_CountsEachStatusOnce()
    {
        var summaries = new List<SubAgentSessionSummary>
        {
            Summary("a", status: SubAgentSessionStatus.Running),
            Summary("b", status: SubAgentSessionStatus.Completed),
            Summary("c", status: SubAgentSessionStatus.Completed),
            Summary("d", status: SubAgentSessionStatus.Failed)
        };

        var counts = SubAgentSessionQuery.CountByStatus(summaries);

        counts.Should().Be(new SubAgentSessionStatusCounts(4, 1, 2, 1));
    }

    [Fact]
    public void SummariesEqual_DetectsEveryRenderedFieldAndOrderChange()
    {
        var id = Guid.NewGuid();
        var baseline = new List<SubAgentSessionSummary> { Summary("a", id: id) };

        SubAgentSessionQuery.SummariesEqual(baseline, [Summary("a", id: id)]).Should().BeTrue();

        // 消息数、状态、预览、标题、顺序任一变化都必须触发重新渲染
        SubAgentSessionQuery.SummariesEqual(baseline, [Summary("a", id: id, messageCount: 2)]).Should().BeFalse();
        SubAgentSessionQuery.SummariesEqual(baseline, [Summary("a", id: id, status: SubAgentSessionStatus.Failed)]).Should().BeFalse();
        SubAgentSessionQuery.SummariesEqual(baseline, [Summary("a", id: id, preview: "p")]).Should().BeFalse();
        SubAgentSessionQuery.SummariesEqual(baseline, [Summary("b", id: id)]).Should().BeFalse();
        SubAgentSessionQuery.SummariesEqual(
            baseline,
            [Summary("a", id: id), Summary("b")]).Should().BeFalse();
        SubAgentSessionQuery.SummariesEqual(
            [Summary("b"), Summary("a", id: id)],
            [Summary("a", id: id), Summary("b")]).Should().BeFalse();
    }

    [Fact]
    public void NeedsDetailReload_OnlyWhenSnapshotMissingOrSummaryMoved()
    {
        var loaded = Summary("a");
        var current = Summary("a");

        SubAgentSessionRefreshPlan.NeedsDetailReload(current, loaded, hasLoadedSnapshot: true)
            .Should().BeFalse();

        // 没有任何已加载快照时必须读
        SubAgentSessionRefreshPlan.NeedsDetailReload(current, null, hasLoadedSnapshot: false)
            .Should().BeTrue();

        current.MessageCount = 2;
        SubAgentSessionRefreshPlan.NeedsDetailReload(current, loaded, true).Should().BeTrue();

        current = Summary("a");
        current.Status = SubAgentSessionStatus.Failed;
        SubAgentSessionRefreshPlan.NeedsDetailReload(current, loaded, true).Should().BeTrue();

        current = Summary("a");
        current.UpdatedAt = loaded.UpdatedAt.AddSeconds(1);
        SubAgentSessionRefreshPlan.NeedsDetailReload(current, loaded, true).Should().BeTrue();
    }

    [Fact]
    public void TranscriptWindow_FollowsTailByDefault()
    {
        var (start, count) = SubAgentTranscriptWindow.Resolve(120, null);

        start.Should().Be(120 - SubAgentTranscriptWindow.PageSize);
        count.Should().Be(SubAgentTranscriptWindow.PageSize);

        var (shortStart, shortCount) = SubAgentTranscriptWindow.Resolve(3, null);
        shortStart.Should().Be(0);
        shortCount.Should().Be(3);
    }

    [Fact]
    public void TranscriptWindow_PinnedStartStaysFixedSoRefreshDoesNotShiftReading()
    {
        var (start, count) = SubAgentTranscriptWindow.Resolve(120, 10);

        start.Should().Be(10);
        count.Should().Be(110);

        // 自动刷新追加消息后固定起点不变，只有尾部变长
        var (afterAppend, afterCount) = SubAgentTranscriptWindow.Resolve(130, 10);
        afterAppend.Should().Be(10);
        afterCount.Should().Be(120);
    }

    [Fact]
    public void TranscriptWindow_ExpandEarlierPagesBackwardsAndClampsAtZero()
    {
        SubAgentTranscriptWindow.ExpandEarlier(120, null)
            .Should().Be(120 - SubAgentTranscriptWindow.PageSize * 2);

        SubAgentTranscriptWindow.ExpandEarlier(120, 40).Should().Be(0);
        SubAgentTranscriptWindow.ExpandEarlier(120, 0).Should().Be(0);
    }

    [Fact]
    public void TranscriptWindow_OutOfRangePinnedStartFallsBackToTail()
    {
        // 会话被外部改写导致固定下标越界：回退到最近一页，而不是渲染空窗口
        var (start, count) = SubAgentTranscriptWindow.Resolve(20, 99);

        start.Should().Be(0);
        count.Should().Be(20);

        SubAgentTranscriptWindow.Resolve(0, 5).Should().Be((0, 0));
    }
}
