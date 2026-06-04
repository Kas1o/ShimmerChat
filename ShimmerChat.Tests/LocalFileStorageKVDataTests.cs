using FluentAssertions;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

public class LocalFileStorageKVDataTests : IDisposable
{
    private readonly LocalFileStorageKVData _kvData;
    private readonly string _kvDataRoot;

    public LocalFileStorageKVDataTests()
    {
        _kvData = new LocalFileStorageKVData();
        _kvDataRoot = _kvData.RootPath;
        _kvData.ClearAll();
    }

    public void Dispose()
    {
        _kvData.ClearAll();
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
        var act = () => _kvData.Read("  ", "key");
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
        var result = _kvData.Read("nonexistent", "key");
        result.Should().BeNull();
    }

    [Fact]
    public void Write_Overwrite_UpdatesValue()
    {
        _kvData.Write("space1", "key1", "v1");
        _kvData.Write("space1", "key1", "v2");

        _kvData.Read("space1", "key1").Should().Be("v2");
    }

    [Fact]
    public void Write_SpaceIsolation_NoCollision()
    {
        _kvData.Write("space_a", "key_x", "value_a");
        _kvData.Write("space_b", "key_x", "value_b");

        _kvData.Read("space_a", "key_x").Should().Be("value_a");
        _kvData.Read("space_b", "key_x").Should().Be("value_b");
    }

    [Fact]
    public void GetAllSpaceIds_ReturnsDistinctSpaces()
    {
        _kvData.Write("s1", "k1", "v1");
        _kvData.Write("s2", "k1", "v2");

        var spaces = _kvData.GetAllSpaceIds().ToList();
        spaces.Should().Contain(["s1", "s2"]);
    }

    [Fact]
    public void GetAllSpaceIds_Empty_ReturnsEmpty()
    {
        var spaces = _kvData.GetAllSpaceIds().ToList();
        spaces.Should().BeEmpty();
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
    public void GetAllKeys_NonexistentSpace_ReturnsEmpty()
    {
        var keys = _kvData.GetAllKeys("nonexistent").ToList();
        keys.Should().BeEmpty();
    }

    [Fact]
    public void GetAllEntries_ReturnsAllForSpace()
    {
        _kvData.Write("space1", "key1", "value1");
        _kvData.Write("space1", "key2", "value2");

        var entries = _kvData.GetAllEntries("space1").ToList();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Key == "key1" && e.Value == "value1");
    }

    [Fact]
    public void BulkWrite_InsertsMultiple()
    {
        var entries = new List<KVDataItem>
        {
            new() { SpaceId = "s1", Key = "k1", Value = "v1" },
            new() { SpaceId = "s1", Key = "k2", Value = "v2" },
        };
        _kvData.BulkWrite(entries);

        _kvData.Read("s1", "k1").Should().Be("v1");
        _kvData.Read("s1", "k2").Should().Be("v2");
    }

    [Fact]
    public void ClearAll_RemovesAllData()
    {
        _kvData.Write("space1", "key1", "value1");

        _kvData.ClearAll();

        _kvData.Read("space1", "key1").Should().BeNull();
    }

    [Fact]
    public void SanitizeFileName_HandlesSpecialChars()
    {
        _kvData.Write("space:test", "key/test", "value");
        _kvData.Read("space:test", "key/test").Should().Be("value");
    }
}
