using FluentAssertions;
using LiteDB;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class LiteDBDebugOutputServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDBDebugOutputService _service;

    public LiteDBDebugOutputServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _service = new LiteDBDebugOutputService(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public void Write_GetEntries_Roundtrip()
    {
        _service.Write("source1", "category1", "hello debug");

        var entries = _service.GetEntries(0, 10);
        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Source.Should().Be("source1");
        entry.Category.Should().Be("category1");
        entry.Content.Should().Be("hello debug");
    }

    [Fact]
    public void GetEntries_FilterByKeyword_MatchesContentCaseInsensitive()
    {
        _service.Write("srcA", "catX", "Hello World");
        _service.Write("srcA", "catY", "nothing here");
        _service.Write("srcB", "catX", "hello again");

        var entries = _service.GetEntries(0, 10, keyword: "HELLO");
        entries.Select(e => e.Content).Should().BeEquivalentTo(["Hello World", "hello again"]);
    }

    [Fact]
    public void GetEntries_FilterByKeyword_MatchesSourceAndCategory()
    {
        _service.Write("SourceAlpha", "cat", "1");
        _service.Write("src", "CategoryBeta", "2");
        _service.Write("src", "cat", "3");

        _service.GetEntries(0, 10, keyword: "alpha").Select(e => e.Content).Should().Equal(["1"]);
        _service.GetEntries(0, 10, keyword: "beta").Select(e => e.Content).Should().Equal(["2"]);
    }

    [Fact]
    public void GetCount_FilterByKeyword()
    {
        _service.Write("srcA", "cat", "foo bar");
        _service.Write("srcA", "cat", "FOO baz");
        _service.Write("srcA", "cat", "qux");

        _service.GetCount(keyword: "foo").Should().Be(2);
        _service.GetCount(keyword: "missing").Should().Be(0);
    }

    [Fact]
    public void GetEntries_FilterByTimeRange_Inclusive()
    {
        _service.Write("src", "cat", "old");
        Thread.Sleep(10);
        var mid = DateTime.UtcNow;
        Thread.Sleep(10);
        _service.Write("src", "cat", "new");
        Thread.Sleep(10);
        _service.Write("src", "cat", "newest");

        var fromNew = _service.GetEntries(0, 10, from: mid);
        fromNew.Select(e => e.Content).Should().BeEquivalentTo(["new", "newest"]);

        var upToNew = _service.GetEntries(0, 10, to: mid);
        upToNew.Select(e => e.Content).Should().Equal(["old"]);
    }

    [Fact]
    public void GetEntries_CombinedFilters_KeywordSourceTime()
    {
        _service.Write("srcA", "cat", "target payload");
        _service.Write("srcA", "cat", "other payload");
        _service.Write("srcB", "cat", "target payload");

        var entries = _service.GetEntries(0, 10, sourceFilter: "srcA", keyword: "target");
        entries.Select(e => e.Content).Should().Equal(["target payload"]);
    }

    [Fact]
    public void GetEntries_Paging_RespectsFilters()
    {
        for (int i = 1; i <= 5; i++)
        {
            _service.Write("srcA", "cat", $"match{i}");
            Thread.Sleep(2);
            _service.Write("srcB", "cat", $"other{i}");
        }

        var page1 = _service.GetEntries(0, 2, keyword: "match");
        page1.Select(e => e.Content).Should().Equal(["match5", "match4"]);

        var page2 = _service.GetEntries(2, 2, keyword: "match");
        page2.Select(e => e.Content).Should().Equal(["match3", "match2"]);

        _service.GetCount(keyword: "match").Should().Be(5);
    }

    [Fact]
    public void DeleteEntry_And_ClearAll()
    {
        _service.Write("src", "cat", "1");
        _service.Write("src", "cat", "2");

        var id = _service.GetEntries(0, 10).Single(e => e.Content == "1").Id;
        _service.DeleteEntry(id).Should().BeTrue();
        _service.GetCount().Should().Be(1);

        _service.ClearAll();
        _service.GetCount().Should().Be(0);
    }

    [Fact]
    public void TrimToRecent_KeepsNewestOnly()
    {
        for (int i = 1; i <= 5; i++)
        {
            _service.Write("src", "cat", $"msg{i}");
            Thread.Sleep(2);
        }

        int deleted = _service.TrimToRecent(2);

        deleted.Should().Be(3);
        var remaining = _service.GetEntries(0, 10).Select(e => e.Content).ToList();
        remaining.Should().Equal(["msg5", "msg4"]);
    }

    [Fact]
    public void TrimToRecent_KeepAtLeastTotal_DeletesNothing()
    {
        _service.Write("src", "cat", "1");
        _service.Write("src", "cat", "2");

        _service.TrimToRecent(5).Should().Be(0);
        _service.GetCount().Should().Be(2);
    }

    [Fact]
    public void TrimToRecent_NonPositive_ClearsAll()
    {
        _service.Write("src", "cat", "1");
        _service.Write("src", "cat", "2");

        _service.TrimToRecent(0).Should().Be(2);
        _service.GetCount().Should().Be(0);
    }

    [Fact]
    public void DeleteOlderThan_DeletesOnlyOlderEntries()
    {
        _service.Write("src", "cat", "old");
        Thread.Sleep(10);
        var mid = DateTime.UtcNow;
        Thread.Sleep(10);
        _service.Write("src", "cat", "new");

        int deleted = _service.DeleteOlderThan(mid);

        deleted.Should().Be(1);
        var remaining = _service.GetEntries(0, 10).Select(e => e.Content).ToList();
        remaining.Should().Equal(["new"]);
    }
}
