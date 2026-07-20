using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class LiteDBKVDataTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDBKVData _kvData;

    public LiteDBKVDataTests()
    {
        _database = new LiteDatabase(":memory:");
        _kvData = new LiteDBKVData(_database, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiteDBKVData>.Instance);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public void Read_NullSpaceId_Throws()
    {
        var act = () => _kvData.Read(null!, "key");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_EmptySpaceId_Throws()
    {
        var act = () => _kvData.Read("", "key");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_NullKey_Throws()
    {
        var act = () => _kvData.Read("space", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_NullValue_Throws()
    {
        var act = () => _kvData.Write("space", "key", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_EmptySpaceId_Throws()
    {
        var act = () => _kvData.Write("", "key", "value");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadWrite_Roundtrip()
    {
        _kvData.Write("space1", "key1", "value1");

        var result = _kvData.Read("space1", "key1");
        result.Should().Be("value1");
    }

    [Fact]
    public void Read_NonExistentKey_ReturnsNull()
    {
        var result = _kvData.Read("space1", "nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public void Write_Overwrite_UpdatesValue()
    {
        _kvData.Write("space1", "key1", "v1");
        _kvData.Write("space1", "key1", "v2");

        var result = _kvData.Read("space1", "key1");
        result.Should().Be("v2");
    }

    [Fact]
    public void Write_SpaceIsolation_NoCollision()
    {
        _kvData.Write("space1", "key1", "value1");
        _kvData.Write("space2", "key1", "value2");

        _kvData.Read("space1", "key1").Should().Be("value1");
        _kvData.Read("space2", "key1").Should().Be("value2");
    }

    [Fact]
    public void GetAllSpaceIds_ReturnsDistinctSpaces()
    {
        _kvData.Write("space1", "k1", "v1");
        _kvData.Write("space2", "k1", "v2");
        _kvData.Write("space1", "k2", "v3");

        var spaces = _kvData.GetAllSpaceIds().ToList();
        spaces.Should().Contain(["space1", "space2"]);
        spaces.Should().HaveCount(2);
    }

    [Fact]
    public void GetAllKeys_ReturnsKeysForSpace()
    {
        _kvData.Write("space1", "key1", "v1");
        _kvData.Write("space1", "key2", "v2");
        _kvData.Write("space2", "key3", "v3");

        var keys = _kvData.GetAllKeys("space1").ToList();
        keys.Should().Contain(["key1", "key2"]);
        keys.Should().HaveCount(2);
    }

    [Fact]
    public void GetAllEntries_ReturnsAllForSpace()
    {
        _kvData.Write("space1", "key1", "value1");
        _kvData.Write("space1", "key2", "value2");

        var entries = _kvData.GetAllEntries("space1").ToList();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Key == "key1" && e.Value == "value1");
        entries.Should().Contain(e => e.Key == "key2" && e.Value == "value2");
    }

    [Fact]
    public void BulkWrite_InsertsMultiple()
    {
        var entries = new List<LiteDBKVData.KVDataEntry>
        {
            new() { SpaceId = "s1", Key = "k1", Value = "v1" },
            new() { SpaceId = "s1", Key = "k2", Value = "v2" },
        };
        _kvData.BulkWrite(entries);

        _kvData.Read("s1", "k1").Should().Be("v1");
        _kvData.Read("s1", "k2").Should().Be("v2");
    }

    [Fact]
    public void BulkWrite_UpdatesExisting()
    {
        _kvData.Write("s1", "k1", "original");
        var entries = new List<LiteDBKVData.KVDataEntry>
        {
            new() { SpaceId = "s1", Key = "k1", Value = "updated" },
        };
        _kvData.BulkWrite(entries);

        _kvData.Read("s1", "k1").Should().Be("updated");
    }

    [Fact]
    public void ClearAll_RemovesAllData()
    {
        _kvData.Write("space1", "key1", "value1");
        _kvData.Write("space2", "key1", "value2");

        _kvData.ClearAll();

        _kvData.Read("space1", "key1").Should().BeNull();
        _kvData.Read("space2", "key1").Should().BeNull();
    }

    [Fact]
    public void GetAllSpaceIds_Empty_ReturnsEmpty()
    {
        var spaces = _kvData.GetAllSpaceIds().ToList();
        spaces.Should().BeEmpty();
    }

    [Fact]
    public void GetAllKeys_NonexistentSpace_ReturnsEmpty()
    {
        var keys = _kvData.GetAllKeys("nonexistent").ToList();
        keys.Should().BeEmpty();
    }
}
