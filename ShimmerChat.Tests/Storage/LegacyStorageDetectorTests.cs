using FluentAssertions;
using LiteDB;
using ShimmerChat.Singletons.Storage;
using ShimmerChatLib;
using ShimmerChatLib.Models;
using Xunit;

namespace ShimmerChat.Tests.Storage;

/// <summary>
/// 首次生成 storage_settings.json 时按旧数据位置推断每个仓库的后端；
/// 同一仓库两边都有数据时直接报错，不猜测、不迁移。
/// </summary>
public sealed class LegacyStorageDetectorTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "shimmer-detector", Guid.NewGuid().ToString("N"));

    public LegacyStorageDetectorTests()
    {
        Directory.CreateDirectory(_base);
    }

    [Fact]
    public void ApplyToDefaults_DetectsEachStoreIndependently()
    {
        WriteLegacyDatabase(kvData: 2, messages: 0, debugOutput: 1);
        Directory.CreateDirectory(Path.Combine(_base, "ChatMessages", Guid.NewGuid().ToString()));

        var settings = StorageSettings.CreateDefaults(null, null);

        LegacyStorageDetector.ApplyToDefaults(settings, _base, legacyConfigPresent: false);

        settings.KVData.GetStorageType().Should().Be(KVStorageType.LiteDB);
        settings.ChatMessage.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        settings.DebugOutput.GetStorageType().Should().Be(KVStorageType.LiteDB);
    }

    [Fact]
    public void ApplyToDefaults_BothSourcesHaveData_ThrowsInsteadOfGuessing()
    {
        WriteLegacyDatabase(kvData: 2, messages: 0, debugOutput: 0);
        Directory.CreateDirectory(Path.Combine(_base, "KVData", "Space1"));

        var settings = StorageSettings.CreateDefaults(null, null);

        var act = () => LegacyStorageDetector.ApplyToDefaults(settings, _base, legacyConfigPresent: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*KVData*")
            .WithMessage("*StorageType*");
    }

    [Fact]
    public void ApplyToDefaults_NoDatabaseButFiles_SelectsFileStorage()
    {
        Directory.CreateDirectory(Path.Combine(_base, "KVData", "Space1"));
        Directory.CreateDirectory(Path.Combine(_base, "ChatMessages", Guid.NewGuid().ToString()));
        Directory.CreateDirectory(Path.Combine(_base, "DebugOutput"));
        File.WriteAllText(Path.Combine(_base, "DebugOutput", Guid.NewGuid().ToString("N") + ".json"), "{}");

        var settings = StorageSettings.CreateDefaults(null, null);

        LegacyStorageDetector.ApplyToDefaults(settings, _base, legacyConfigPresent: false);

        settings.KVData.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        settings.ChatMessage.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        settings.DebugOutput.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
    }

    [Fact]
    public void ApplyToDefaults_ExplicitLegacyConfigWins()
    {
        WriteLegacyDatabase(kvData: 2, messages: 2, debugOutput: 2);

        var settings = StorageSettings.CreateDefaults(
            new KVDataStorageConfig { StorageType = "LocalFileStorage" },
            null);

        LegacyStorageDetector.ApplyToDefaults(settings, _base, legacyConfigPresent: true);

        settings.KVData.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        settings.ChatMessage.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        settings.DebugOutput.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
    }

    [Fact]
    public void ApplyToDefaults_NoLegacyData_KeepsDefaults()
    {
        var settings = StorageSettings.CreateDefaults(null, null);

        LegacyStorageDetector.ApplyToDefaults(settings, _base, legacyConfigPresent: false);

        settings.KVData.GetStorageType().Should().Be(KVStorageType.LiteDB);
        settings.ChatMessage.GetStorageType().Should().Be(KVStorageType.LiteDB);
        settings.DebugOutput.GetStorageType().Should().Be(KVStorageType.LiteDB);
    }

    private void WriteLegacyDatabase(int kvData, int messages, int debugOutput)
    {
        Directory.CreateDirectory(Path.Combine(_base, "LiteDBKVData"));
        using var database = new LiteDatabase(
            $"Filename={Path.Combine(_base, "LiteDBKVData", "data.db")};Connection=direct");

        var kv = database.GetCollection<BsonDocument>("kvdata");
        for (var i = 0; i < kvData; i++)
            kv.Insert(new BsonDocument { ["SpaceId"] = "Space1", ["Key"] = $"k{i}", ["Value"] = "v" });

        var messageCollection = database.GetCollection<BsonDocument>("messages");
        for (var i = 0; i < messages; i++)
            messageCollection.Insert(new BsonDocument { ["MessageGuid"] = Guid.NewGuid(), ["ChatGuid"] = Guid.NewGuid() });

        var debug = database.GetCollection<BsonDocument>("debug_output");
        for (var i = 0; i < debugOutput; i++)
            debug.Insert(new BsonDocument { ["EntryId"] = Guid.NewGuid(), ["Timestamp"] = DateTime.UtcNow });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }
}
