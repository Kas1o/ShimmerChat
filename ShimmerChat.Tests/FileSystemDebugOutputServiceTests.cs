using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class FileSystemDebugOutputServiceTests : IDisposable
{
    private readonly FileSystemDebugOutputService _service;
    private readonly string _root;

    public FileSystemDebugOutputServiceTests()
    {
        _service = new FileSystemDebugOutputService(NullLogger<FileSystemDebugOutputService>.Instance);
        _root = _service.RootPath;
        _service.ClearAll();
    }

    public void Dispose()
    {
        _service.ClearAll();
    }

    [Fact]
    public void Write_GetEntries_Roundtrip()
    {
        _service.Write("source1", "category1", "hello debug");

        var entries = _service.GetEntries(0, 10);
        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Id.Should().NotBeEmpty();
        entry.Source.Should().Be("source1");
        entry.Category.Should().Be("category1");
        entry.Content.Should().Be("hello debug");
        entry.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void GetEntries_ReturnsNewestFirst()
    {
        _service.Write("src", "cat", "first");
        Thread.Sleep(5);
        _service.Write("src", "cat", "second");
        Thread.Sleep(5);
        _service.Write("src", "cat", "third");

        var entries = _service.GetEntries(0, 10);
        entries.Select(e => e.Content).Should().Equal(["third", "second", "first"]);
    }

    [Fact]
    public void GetEntries_Paging_SkipsAndTakes()
    {
        for (int i = 1; i <= 5; i++)
        {
            _service.Write("src", "cat", $"msg{i}");
            Thread.Sleep(2);
        }

        // 按时间倒序: msg5, msg4, msg3, msg2, msg1
        var page1 = _service.GetEntries(0, 2);
        page1.Select(e => e.Content).Should().Equal(["msg5", "msg4"]);

        var page2 = _service.GetEntries(2, 2);
        page2.Select(e => e.Content).Should().Equal(["msg3", "msg2"]);

        var page3 = _service.GetEntries(4, 2);
        page3.Select(e => e.Content).Should().Equal(["msg1"]);
    }

    [Fact]
    public void GetEntries_FilterBySource()
    {
        _service.Write("sourceA", "cat", "a1");
        _service.Write("sourceB", "cat", "b1");
        _service.Write("sourceA", "cat", "a2");

        var entries = _service.GetEntries(0, 10, sourceFilter: "sourceA");
        entries.Select(e => e.Content).Should().BeEquivalentTo(["a1", "a2"]);
    }

    [Fact]
    public void GetEntries_FilterByCategory()
    {
        _service.Write("src", "catA", "a1");
        _service.Write("src", "catB", "b1");
        _service.Write("src", "catA", "a2");

        var entries = _service.GetEntries(0, 10, categoryFilter: "catA");
        entries.Select(e => e.Content).Should().BeEquivalentTo(["a1", "a2"]);
    }

    [Fact]
    public void GetEntries_FilterSourceAndCategory()
    {
        _service.Write("srcA", "catX", "a-x");
        _service.Write("srcA", "catY", "a-y");
        _service.Write("srcB", "catX", "b-x");

        var entries = _service.GetEntries(0, 10, sourceFilter: "srcA", categoryFilter: "catX");
        entries.Select(e => e.Content).Should().Equal(["a-x"]);
    }

    [Fact]
    public void GetCount_RespectsFilters()
    {
        _service.Write("srcA", "catX", "1");
        _service.Write("srcA", "catY", "2");
        _service.Write("srcB", "catX", "3");

        _service.GetCount().Should().Be(3);
        _service.GetCount(sourceFilter: "srcA").Should().Be(2);
        _service.GetCount(categoryFilter: "catX").Should().Be(2);
        _service.GetCount(sourceFilter: "srcA", categoryFilter: "catX").Should().Be(1);
        _service.GetCount(sourceFilter: "missing").Should().Be(0);
    }

    [Fact]
    public void GetSources_ReturnsDistinctSorted()
    {
        _service.Write("srcB", "cat", "1");
        _service.Write("srcA", "cat", "2");
        _service.Write("srcB", "cat", "3");

        _service.GetSources().Should().Equal(["srcA", "srcB"]);
    }

    [Fact]
    public void GetCategories_ReturnsDistinctSorted()
    {
        _service.Write("src", "catB", "1");
        _service.Write("src", "catA", "2");
        _service.Write("src", "catB", "3");

        _service.GetCategories().Should().Equal(["catA", "catB"]);
    }

    [Fact]
    public void DeleteEntry_Existing_ReturnsTrue()
    {
        _service.Write("src", "cat", "content");
        var id = _service.GetEntries(0, 10).Single().Id;

        _service.DeleteEntry(id).Should().BeTrue();
        _service.GetCount().Should().Be(0);
    }

    [Fact]
    public void DeleteEntry_NonExisting_ReturnsFalse()
    {
        _service.DeleteEntry(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void ClearAll_RemovesAllEntries()
    {
        _service.Write("src1", "cat", "1");
        _service.Write("src2", "cat", "2");

        _service.ClearAll();

        _service.GetCount().Should().Be(0);
        _service.GetSources().Should().BeEmpty();
        _service.GetCategories().Should().BeEmpty();
    }
}
