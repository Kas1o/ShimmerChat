using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;
using ShimmerChat.Singletons.Storage;
using ShimmerChatLib.Interface;
using Xunit;

namespace ShimmerChat.Tests.Storage;

/// <summary>
/// 调试输出仓库的双向迁移：条目 Id / 时间戳保持不变，重复执行以目标清空为前提。
/// </summary>
public sealed class DebugOutputMigrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shimmer-debug-migration", Guid.NewGuid().ToString("N"));
    private readonly LiteDatabase _database;
    private readonly FileSystemDebugOutputService _fileStore;
    private readonly LiteDBDebugOutputService _liteStore;
    private readonly DebugOutputMigrationService _service;

    public DebugOutputMigrationServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _fileStore = new FileSystemDebugOutputService(
            NullLogger<FileSystemDebugOutputService>.Instance,
            Path.Combine(_root, "debug_output"));
        _liteStore = new LiteDBDebugOutputService(_database);
        _service = new DebugOutputMigrationService(
            _fileStore,
            _liteStore,
            NullLogger<DebugOutputMigrationService>.Instance);
    }

    [Fact]
    public void MigrateFileToLite_PreservesEntryIds()
    {
        _fileStore.Write("source-a", "cat", "one");
        _fileStore.Write("source-b", "cat", "two");
        var expectedIds = _fileStore.ReadAll().Select(x => x.Id).OrderBy(x => x).ToArray();

        var count = _service.MigrateFileToLite();

        count.Should().Be(2);
        _liteStore.GetCount().Should().Be(2);
        _liteStore.ReadAll().Select(x => x.Id).OrderBy(x => x).Should().Equal(expectedIds);
        _fileStore.GetCount().Should().Be(2);
    }

    [Fact]
    public void MigrateFileToLite_ClearSource_EmptiesFileStore()
    {
        _fileStore.Write("source", "cat", "content");

        _service.MigrateFileToLite(clearSource: true);

        _fileStore.GetCount().Should().Be(0);
        _liteStore.GetCount().Should().Be(1);
    }

    [Fact]
    public void MigrateLiteToFile_PreservesEntryIds()
    {
        _liteStore.Write("source", "cat", "content");
        var expected = _liteStore.ReadAll().Single().Id;

        var count = _service.MigrateLiteToFile();

        count.Should().Be(1);
        _fileStore.ReadAll().Single().Id.Should().Be(expected);
    }

    [Fact]
    public void MigrateLiteToFile_ClearSource_EmptiesLiteStore()
    {
        _liteStore.Write("source", "cat", "content");

        _service.MigrateLiteToFile(clearSource: true);

        _liteStore.GetCount().Should().Be(0);
        _fileStore.GetCount().Should().Be(1);
    }

    [Fact]
    public void MigrateFileToLite_WhenTargetIsUnavailable_Throws()
    {
        _fileStore.Write("source", "cat", "content");
        _database.Dispose();

        var act = () => _service.MigrateFileToLite();

        act.Should().Throw<Exception>();
    }

    public void Dispose()
    {
        try
        {
            _database.Dispose();
        }
        catch
        {
            // 已被失败用例释放
        }

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }
}
