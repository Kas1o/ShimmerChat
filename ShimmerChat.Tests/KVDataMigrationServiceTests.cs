using FluentAssertions;
using LiteDB;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class KVDataMigrationServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDBKVData _liteDB;
    private readonly TestLocalFileStorage _localFile;
    private readonly KVDataMigrationService _migrationService;

    private class TestLocalFileStorage : LocalFileStorageKVData
    {
        public new string RootPath => base.RootPath;
    }

    public KVDataMigrationServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _liteDB = new LiteDBKVData(_database);
        _localFile = new TestLocalFileStorage();
        _localFile.ClearAll();
        _migrationService = new KVDataMigrationService(_localFile, _liteDB);
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
