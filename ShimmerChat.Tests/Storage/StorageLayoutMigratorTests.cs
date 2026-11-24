using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons.Storage;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using Xunit;

namespace ShimmerChat.Tests.Storage;

/// <summary>
/// 旧目录结构 → Data/ 的一次性布局迁移：拆分 LiteDB、合并向量库、复制文件存储与图像，
/// 保留旧目录、幂等、有完成标记。
/// </summary>
public sealed class StorageLayoutMigratorTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "shimmer-layout-migration", Guid.NewGuid().ToString("N"));
    private readonly DataPaths _paths;

    public StorageLayoutMigratorTests()
    {
        Directory.CreateDirectory(_base);
        _paths = new DataPaths(Path.Combine(_base, "Data"));
    }

    private StorageLayoutMigrator CreateMigrator() => new(_base, _paths);

    [Fact]
    public void Migrate_LegacyLayout_SplitsDatabasesAndCopiesFiles()
    {
        var legacy = CreateLegacyLayout();

        CreateMigrator().Migrate(legacyVectorRootPath: null, KVStorageType.LiteDB).Should().BeTrue();

        CountDocuments(_paths.KvDataDatabase, "kvdata").Should().Be(2);
        CountDocuments(_paths.ChatMessageDatabase, "messages").Should().Be(2);
        CountDocuments(_paths.DebugOutputDatabase, "debug_output").Should().Be(1);

        File.Exists(_paths.LayoutMigrationMarkerFile).Should().BeTrue();
        File.Exists(_paths.MigrationMarkerFile).Should().BeTrue();

        File.ReadAllText(Path.Combine(_paths.LocalKvDataDirectory, "Space1", "key1.json")).Should().Be("value-1");
        File.Exists(Path.Combine(_paths.LocalChatMessageDirectory, legacy.ChatGuid.ToString(), legacy.MessageId + ".json")).Should().BeTrue();
        File.Exists(Path.Combine(_paths.LocalDebugOutputDirectory, legacy.DebugEntryId.ToString("N") + ".json")).Should().BeTrue();
        File.Exists(Path.Combine(_paths.UserUploadImageDirectory, "avatar.png")).Should().BeTrue();
        File.Exists(_paths.MemoryDatabase).Should().BeTrue();
        CountDocuments(_paths.MemoryDatabase, "records").Should().Be(1);

        ReadMemoryStoreIdentity().Should().Be($"LiteDb:{_paths.VectorDatabase}");

        // 旧目录必须原样保留
        Directory.Exists(Path.Combine(_base, "LiteDBKVData")).Should().BeTrue();
        Directory.Exists(Path.Combine(_base, "KVData")).Should().BeTrue();
        Directory.Exists(Path.Combine(_base, "ChatMessages")).Should().BeTrue();
        Directory.Exists(Path.Combine(_base, "UserUploadImage")).Should().BeTrue();
    }

    [Fact]
    public async Task Migrate_LegacyVectorDatabases_AreMergedIntoSingleVectorDb()
    {
        CreateLegacyLayout();

        CreateMigrator().Migrate(null, KVStorageType.LiteDB);

        await using var store = new LiteDbVectorStore(_paths, NullLogger<LiteDbVectorStore>.Instance);
        var collections = await store.ListCollectionsAsync();

        collections.Should().ContainSingle();
        collections[0].Name.Should().Be("memory");
        collections[0].Count.Should().Be(1);
        collections[0].Options.Dimensions.Should().Be(3);

        var hits = await store.SearchAsync("memory", new float[] { 1, 0, 0 }, topK: 1);
        hits.Single().Id.Should().Be("record-1");

        Directory.GetFiles(_paths.LiteDbDirectory, "*.db")
            .Select(Path.GetFileName)
            .Should().Contain(DataPaths.VectorDatabaseFileName);
    }

    [Fact]
    public void Migrate_WhenLegacyWasLiteDb_MarksThreeStoresAsAlreadyMigrated()
    {
        CreateLegacyLayout();

        CreateMigrator().Migrate(null, KVStorageType.LiteDB);

        var marker = new StorageMigrationMarker(_paths);

        marker.IsMigrationCompleted(StorageStores.KvData, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
        marker.IsMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
        marker.IsMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeTrue();
    }

    [Fact]
    public void Migrate_WhenLegacyWasFileStorage_DoesNotFakeLiteDbMigration()
    {
        CreateLegacyLayout(createLiteDb: false);

        CreateMigrator().Migrate(null, KVStorageType.LocalFileStorage);

        var marker = new StorageMigrationMarker(_paths);
        marker.IsMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeFalse();
        marker.IsMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeFalse();
    }

    [Fact]
    public void Migrate_WhenLegacyLiteDbIsEmpty_DoesNotBlockFileMigration()
    {
        CreateLegacyLayout(createLiteDb: false);

        Directory.CreateDirectory(Path.Combine(_base, "LiteDBKVData"));
        using (var empty = new LiteDatabase($"Filename={Path.Combine(_base, "LiteDBKVData", "data.db")};Connection=direct"))
        {
            // 旧版每次启动都会创建 data.db，即使实际使用的是文件后端；空库不能当成"已迁移"。
            _ = empty.GetCollection<BsonDocument>("kvdata").Count();
        }

        CreateMigrator().Migrate(null, KVStorageType.LiteDB);

        var marker = new StorageMigrationMarker(_paths);
        marker.IsMigrationCompleted(StorageStores.ChatMessage, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeFalse();
        marker.IsMigrationCompleted(StorageStores.DebugOutput, KVStorageType.LocalFileStorage, KVStorageType.LiteDB).Should().BeFalse();
    }

    [Fact]
    public void Migrate_IsIdempotent_SecondRunSkips()
    {
        CreateLegacyLayout();

        CreateMigrator().Migrate(null, KVStorageType.LiteDB).Should().BeTrue();
        var firstCount = CountDocuments(_paths.KvDataDatabase, "kvdata");

        CreateMigrator().Migrate(null, KVStorageType.LiteDB).Should().BeFalse();

        CountDocuments(_paths.KvDataDatabase, "kvdata").Should().Be(firstCount);
    }

    [Fact]
    public void Migrate_DoesNotOverwriteExistingTargetFiles()
    {
        CreateLegacyLayout();
        Directory.CreateDirectory(_paths.UserUploadImageDirectory);
        var target = Path.Combine(_paths.UserUploadImageDirectory, "avatar.png");
        File.WriteAllText(target, "newer");

        CreateMigrator().Migrate(null, KVStorageType.LiteDB);

        File.ReadAllText(target).Should().Be("newer");
    }

    [Fact]
    public void Migrate_FreshInstall_WritesMarkerWithoutCreatingDatabases()
    {
        CreateMigrator().Migrate(null, KVStorageType.LiteDB).Should().BeTrue();

        File.Exists(_paths.LayoutMigrationMarkerFile).Should().BeTrue();
        Directory.GetFiles(_paths.LiteDbDirectory, "*.db").Should().BeEmpty();
    }

    private LegacyLayout CreateLegacyLayout(bool createLiteDb = true)
    {
        var chatGuid = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var debugEntryId = Guid.NewGuid();

        if (createLiteDb)
        {
            Directory.CreateDirectory(Path.Combine(_base, "LiteDBKVData"));
            using var database = new LiteDatabase($"Filename={Path.Combine(_base, "LiteDBKVData", "data.db")};Connection=direct");

            var kv = database.GetCollection<BsonDocument>("kvdata");
            kv.Insert(new BsonDocument { ["SpaceId"] = "Space1", ["Key"] = "k1", ["Value"] = "v1" });
            kv.Insert(new BsonDocument { ["SpaceId"] = "Space1", ["Key"] = "k2", ["Value"] = "v2" });

            var messages = database.GetCollection<BsonDocument>("messages");
            for (var i = 0; i < 2; i++)
            {
                messages.Insert(new BsonDocument
                {
                    ["MessageGuid"] = Guid.NewGuid(),
                    ["ChatGuid"] = chatGuid,
                    ["Order"] = (long)i,
                    ["Timestamp"] = DateTime.UtcNow,
                    ["Sender"] = "user",
                    ["MessageJson"] = "{}"
                });
            }

            database.GetCollection<BsonDocument>("debug_output").Insert(new BsonDocument
            {
                ["EntryId"] = debugEntryId,
                ["Timestamp"] = DateTime.UtcNow,
                ["Source"] = "test",
                ["Category"] = "test",
                ["Content"] = "hello"
            });
        }

        Directory.CreateDirectory(Path.Combine(_base, "KVData", "Space1"));
        File.WriteAllText(Path.Combine(_base, "KVData", "Space1", "key1.json"), "value-1");
        File.WriteAllText(Path.Combine(_base, "KVData", ".migration_marker"), """
        { "SourceStorage": "LocalFileStorage", "TargetStorage": "LiteDB", "MigratedCount": 1, "Version": 1 }
        """);

        Directory.CreateDirectory(Path.Combine(_base, "ChatMessages", chatGuid.ToString()));
        File.WriteAllText(Path.Combine(_base, "ChatMessages", chatGuid.ToString(), messageId + ".json"), "{}");

        Directory.CreateDirectory(Path.Combine(_base, "DebugOutput"));
        File.WriteAllText(Path.Combine(_base, "DebugOutput", debugEntryId.ToString("N") + ".json"), "{}");

        Directory.CreateDirectory(Path.Combine(_base, "UserUploadImage"));
        File.WriteAllText(Path.Combine(_base, "UserUploadImage", "avatar.png"), "old");

        Directory.CreateDirectory(Path.Combine(_base, "Memory"));
        using (var memory = new LiteDatabase($"Filename={Path.Combine(_base, "Memory", "records.db")};Connection=direct"))
        {
            memory.GetCollection<BsonDocument>("records").Insert(new BsonDocument
            {
                ["_id"] = "mem-1",
                ["AgentId"] = "agent",
                ["Content"] = "remember",
                ["Tags"] = string.Empty,
                ["Version"] = 1L
            });

            memory.GetCollection<BsonDocument>("model_state").Insert(new BsonDocument
            {
                ["_id"] = "model-key",
                ["ModelName"] = "test-model",
                ["BaseUrl"] = "https://example.invalid",
                ["StoreIdentity"] = $"LiteDb:{Path.GetFullPath(Path.Combine(_base, "VectorStore"))}",
                ["Dimensions"] = 3,
                ["BuiltRevision"] = 0L,
                ["Count"] = 1,
                ["Status"] = "ready"
            });
        }

        Directory.CreateDirectory(Path.Combine(_base, "VectorStore"));
        using (var vectors = new LiteDatabase($"Filename={Path.Combine(_base, "VectorStore", "default.db")};Connection=direct"))
        {
            vectors.GetCollection<BsonDocument>("_meta").Insert(new BsonDocument
            {
                ["_id"] = "memory",
                ["Physical"] = "v_test",
                ["Dimensions"] = 3,
                ["Metric"] = 0,
                ["Encoding"] = 0,
                ["EmbeddingModel"] = "test-model",
                ["Owner"] = "default",
                ["SchemaVersion"] = 1,
                ["CreatedAtUnixMs"] = 0L
            });

            vectors.GetCollection<BsonDocument>("v_test").Insert(new BsonDocument
            {
                ["_id"] = "record-1",
                ["Partition"] = "agent",
                ["CreatedAtUnixMs"] = 0L,
                ["PayloadJson"] = null,
                ["Vector"] = new byte[3 * sizeof(float)]
            });
        }

        return new LegacyLayout(chatGuid, messageId, debugEntryId);
    }

    private string? ReadMemoryStoreIdentity()
    {
        using var database = new LiteDatabase($"Filename={_paths.MemoryDatabase};Connection=direct");
        var state = database.GetCollection<BsonDocument>("model_state").FindById("model-key");
        return state is not null && state.TryGetValue("StoreIdentity", out var value) && value.IsString
            ? value.AsString
            : null;
    }

    private static int CountDocuments(string databasePath, string collection)
    {
        using var database = new LiteDatabase($"Filename={databasePath};Connection=direct");
        return database.GetCollection<BsonDocument>(collection).Count();
    }

    private sealed record LegacyLayout(Guid ChatGuid, Guid MessageId, Guid DebugEntryId);

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
