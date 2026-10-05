using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using ShimmerChat.Singletons;
using System.IO;

namespace ShimmerChat.Tests;

public class KVDataMigrationServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDBKVData _liteDB;
    private readonly TestLocalFileStorage _localFile;
    private readonly KVDataMigrationService _migrationService;

    private class TestLocalFileStorage(ILogger<LocalFileStorageKVData> logger)
        : LocalFileStorageKVData(logger)
    {
        public new string RootPath => base.RootPath;
    }

    public KVDataMigrationServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _liteDB = new LiteDBKVData(_database, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiteDBKVData>.Instance);
        _localFile = new TestLocalFileStorage(Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalFileStorageKVData>.Instance);
        _localFile.ClearAll();
        _migrationService = new KVDataMigrationService(_localFile, _liteDB, Microsoft.Extensions.Logging.Abstractions.NullLogger<KVDataMigrationService>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
        _localFile.ClearAll();
    }

    [Fact]
    public void MigrateToLiteDB_CopiesAllEntries()
    {
        _localFile.Write("s1", "k1", "v1");
        _localFile.Write("s1", "k2", "v2");
        _localFile.Write("s2", "k1", "v3");

        var count = _migrationService.MigrateToLiteDB();

        count.Should().Be(3);
        _liteDB.Read("s1", "k1").Should().Be("v1");
        _liteDB.Read("s1", "k2").Should().Be("v2");
        _liteDB.Read("s2", "k1").Should().Be("v3");
    }

    [Fact]
    public void MigrateToLiteDB_EmptySource_ReturnsZero()
    {
        var count = _migrationService.MigrateToLiteDB();
        count.Should().Be(0);
    }

    [Fact]
    public void MigrateToLiteDB_ClearSource_RemovesLocalData()
    {
        _localFile.Write("s1", "k1", "v1");
        _localFile.Write("s1", "k2", "v2");

        var count = _migrationService.MigrateToLiteDB(clearSource: true);

        count.Should().Be(2);
        _localFile.Read("s1", "k1").Should().BeNull();
        _localFile.Read("s1", "k2").Should().BeNull();
    }

    [Fact]
    public void MigrateToLiteDB_NoClearSource_PreservesLocalData()
    {
        _localFile.Write("s1", "k1", "v1");

        _migrationService.MigrateToLiteDB(clearSource: false);

        _localFile.Read("s1", "k1").Should().Be("v1");
    }

    [Fact]
    public void MigrateToLocalFileStorage_CopiesAllEntries()
    {
        _liteDB.Write("s1", "k1", "v1");
        _liteDB.Write("s1", "k2", "v2");
        _liteDB.Write("s2", "k1", "v3");

        var count = _migrationService.MigrateToLocalFileStorage();

        count.Should().Be(3);
        _localFile.Read("s1", "k1").Should().Be("v1");
        _localFile.Read("s1", "k2").Should().Be("v2");
        _localFile.Read("s2", "k1").Should().Be("v3");
    }

    [Fact]
    public void MigrateToLocalFileStorage_EmptySource_ReturnsZero()
    {
        var count = _migrationService.MigrateToLocalFileStorage();
        count.Should().Be(0);
    }

    [Fact]
    public void MigrateToLocalFileStorage_ClearSource_RemovesLiteDBData()
    {
        _liteDB.Write("s1", "k1", "v1");

        var count = _migrationService.MigrateToLocalFileStorage(clearSource: true);

        count.Should().Be(1);
        _liteDB.Read("s1", "k1").Should().BeNull();
    }

    [Fact]
    public void SyncStorages_MergesBothDirections()
    {
        _localFile.Write("s1", "local_only", "local_val");
        _liteDB.Write("s1", "litedb_only", "litedb_val");

        var count = _migrationService.SyncStorages();

        count.Should().Be(2);
        _liteDB.Read("s1", "local_only").Should().Be("local_val");
        _localFile.Read("s1", "litedb_only").Should().Be("litedb_val");
    }

    [Fact]
    public void SyncStorages_Idempotent()
    {
        _localFile.Write("s1", "shared", "value");
        _liteDB.Write("s1", "shared", "value");

        var count = _migrationService.SyncStorages();

        count.Should().Be(0); // nothing new to sync
    }

    [Fact]
    public void SyncStorages_Empty_ReturnsZero()
    {
        var count = _migrationService.SyncStorages();
        count.Should().Be(0);
    }

    /// <summary>
    /// 直接插入带有显式 Value: null 字段的文档，模拟 LiteDB 中真实存在的 null 值条目
    /// （BulkWrite(null) 会被 LiteDB 省略字段，反序列化后为空字符串而非 null）
    /// </summary>
    private void InsertRawNullValueEntry(string spaceId, string key)
    {
        _database.GetCollection("kvdata").Insert(new BsonDocument
        {
            ["_id"] = ObjectId.NewObjectId(),
            ["SpaceId"] = spaceId,
            ["Key"] = key,
            ["Value"] = BsonValue.Null
        });
    }

    [Fact]
    public void MigrateToLocalFileStorage_SkipsNullValueEntriesAndContinues()
    {
        _liteDB.Write("s1", "k1", "v1");
        InsertRawNullValueEntry("s2", "null_key");
        _liteDB.Write("s3", "k1", "v3");

        var count = _migrationService.MigrateToLocalFileStorage();

        count.Should().Be(2);
        _localFile.Read("s1", "k1").Should().Be("v1");
        _localFile.Read("s3", "k1").Should().Be("v3");
        _localFile.Read("s2", "null_key").Should().BeNull();
    }

    [Fact]
    public void MigrateToLocalFileStorage_WithNullValueEntry_ClearSourceStillClears()
    {
        _liteDB.Write("s1", "k1", "v1");
        InsertRawNullValueEntry("s2", "null_key");

        var count = _migrationService.MigrateToLocalFileStorage(clearSource: true);

        count.Should().Be(1);
        _localFile.Read("s1", "k1").Should().Be("v1");
        // null 是合法数据状态（非失败），不阻止清源；null 条目无法迁入文件存储，随源清空
        _liteDB.GetAllKeys("s1").Should().BeEmpty();
        _liteDB.GetAllKeys("s2").Should().BeEmpty();
    }

    [Fact]
    public void SyncStorages_SkipsNullValueEntriesFromLiteDB()
    {
        _liteDB.Write("s1", "litedb_only", "litedb_val");
        InsertRawNullValueEntry("s2", "null_key");

        var count = _migrationService.SyncStorages();

        count.Should().Be(1);
        _localFile.Read("s1", "litedb_only").Should().Be("litedb_val");
        _localFile.Read("s2", "null_key").Should().BeNull();
    }

    [Fact]
    public void MigrateToLocalFileStorage_WriteFailure_ThrowsAndPreservesSource()
    {
        _liteDB.Write("s1", "k1", "v1");
        _liteDB.Write("s2", "k2", "v2");
        // 在目标文件存储中创建一个与数据文件同名的目录，使 File.WriteAllText 必然失败
        Directory.CreateDirectory(Path.Combine(_localFile.RootPath, "s2", "k2.json"));

        var act = () => _migrationService.MigrateToLocalFileStorage(clearSource: true);

        // 迁移失败必须抛异常（fail-fast），调用方不得写入迁移标记
        act.Should().Throw<InvalidOperationException>();
        // 源数据保留：即使请求了 clearSource，失败时也不得清空 LiteDB
        _liteDB.GetAllKeys("s1").Should().Contain("k1");
        _liteDB.GetAllKeys("s2").Should().Contain("k2");
    }

    [Fact]
    public void GetLocalFileStorageCount_ReturnsCorrect()
    {
        _localFile.Write("s1", "k1", "v1");
        _localFile.Write("s1", "k2", "v2");
        _localFile.Write("s2", "k1", "v3");

        _migrationService.GetLocalFileStorageCount().Should().Be(3);
    }

    [Fact]
    public void GetLocalFileStorageCount_Empty_ReturnsZero()
    {
        _migrationService.GetLocalFileStorageCount().Should().Be(0);
    }

    [Fact]
    public void GetLiteDBCount_ReturnsCorrect()
    {
        _liteDB.Write("s1", "k1", "v1");
        _liteDB.Write("s1", "k2", "v2");

        _migrationService.GetLiteDBCount().Should().Be(2);
    }

    [Fact]
    public void GetLiteDBCount_Empty_ReturnsZero()
    {
        _migrationService.GetLiteDBCount().Should().Be(0);
    }
}
