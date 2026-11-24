using FluentAssertions;
using ShimmerChat.Singletons.Storage;
using ShimmerChatLib;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using Xunit;

namespace ShimmerChat.Tests.Storage;

/// <summary>
/// 存储后端迁移标记：按仓库独立记录，旧版单对象标记只读兼容。
/// </summary>
public sealed class StorageMigrationMarkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shimmer-marker-tests", Guid.NewGuid().ToString("N"));
    private readonly DataPaths _paths;

    public StorageMigrationMarkerTests()
    {
        _paths = new DataPaths(Path.Combine(_root, "Data"));
        Directory.CreateDirectory(_paths.Root);
    }

    [Fact]
    public void MarkMigrationCompleted_IsScopedPerStore()
    {
        var marker = new StorageMigrationMarker(_paths);

        marker.MarkMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, 3, false);

        marker.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
        marker.IsMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeFalse();
        marker.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LiteDB, KVStorageType.LocalFileStorage).Should().BeFalse();
    }

    [Fact]
    public void MarkMigrationCompleted_ReplacesSameStoreAndDirection()
    {
        var marker = new StorageMigrationMarker(_paths);

        marker.MarkMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, 1, false);
        marker.MarkMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, 5, true);

        var records = marker.GetRecords();
        records.Should().ContainSingle();
        records[0].MigratedCount.Should().Be(5);
        records[0].SourceCleared.Should().BeTrue();
    }

    [Fact]
    public void LegacyMarker_IsReadAsKvDataRecord()
    {
        var legacy = Path.Combine(_root, "KVData", ".migration_marker");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """
        {
          "MigrationTime": "2026-01-01T00:00:00Z",
          "SourceStorage": "LocalFileStorage",
          "TargetStorage": "LiteDB",
          "MigratedCount": 9,
          "SourceCleared": false,
          "Version": 1
        }
        """);

        var marker = new StorageMigrationMarker(_paths, [legacy]);

        marker.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
        marker.GetRecords().Single().MigratedCount.Should().Be(9);
    }

    [Fact]
    public void MarkMigrationCompleted_WritesNewFileAndKeepsLegacyFile()
    {
        var legacy = Path.Combine(_root, "KVData", ".migration_marker");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """
        { "SourceStorage": "LocalFileStorage", "TargetStorage": "LiteDB", "MigratedCount": 1, "Version": 1 }
        """);

        var marker = new StorageMigrationMarker(_paths, [legacy]);
        marker.MarkMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB, 2, false);

        File.Exists(_paths.MigrationMarkerFile).Should().BeTrue();
        File.Exists(legacy).Should().BeTrue();

        var reloaded = new StorageMigrationMarker(_paths, [legacy]);
        reloaded.GetRecords().Should().HaveCount(2);
        reloaded.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
        reloaded.IsMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
    }

    [Fact]
    public void CorruptMarker_ThrowsInsteadOfReRunningMigration()
    {
        File.WriteAllText(_paths.MigrationMarkerFile, "{ broken");

        var marker = new StorageMigrationMarker(_paths);

        var act = () => marker.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ClearMarker_RemovesNewAndLegacyFiles()
    {
        var legacy = Path.Combine(_root, "KVData", ".migration_marker");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """{ "SourceStorage": "LocalFileStorage", "TargetStorage": "LiteDB" }""");
        File.WriteAllText(_paths.MigrationMarkerFile, """{ "Version": 2, "Migrations": [] }""");

        var marker = new StorageMigrationMarker(_paths, [legacy]);
        marker.ClearMarker();

        File.Exists(_paths.MigrationMarkerFile).Should().BeFalse();
        File.Exists(legacy).Should().BeFalse();
        marker.GetRecords().Should().BeEmpty();
    }

    public void Dispose()
    {
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
