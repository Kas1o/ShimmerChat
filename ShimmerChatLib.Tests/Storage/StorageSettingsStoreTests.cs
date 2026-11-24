using System.Text.Json;
using FluentAssertions;
using ShimmerChatLib;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using Xunit;

namespace ShimmerChatLib.Tests.Storage;

/// <summary>
/// storage_settings.json 是存储配置的唯一事实来源：
/// 缺失时生成、存在时只读、坏内容显式失败，且三个仓库各自独立。
/// </summary>
public sealed class StorageSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shimmer-storage-settings", Guid.NewGuid().ToString("N"));
    private readonly DataPaths _paths;
    private readonly StorageSettingsStore _store;

    public StorageSettingsStoreTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new DataPaths(Path.Combine(_root, "Data"));
        _store = new StorageSettingsStore(_paths);
    }

    [Fact]
    public void LoadOrCreate_MissingFile_GeneratesDefaultsAndPersistsThem()
    {
        var defaults = StorageSettings.CreateDefaults(legacyStore: null, legacyVectorStore: null);

        var loaded = _store.LoadOrCreate(defaults, out var created);

        created.Should().BeTrue();
        File.Exists(_paths.StorageSettingsFile).Should().BeTrue();
        loaded.KVData.StorageType.Should().Be("LiteDB");
        loaded.VectorStore.Provider.Should().Be("LiteDb");

        using var document = JsonDocument.Parse(File.ReadAllText(_paths.StorageSettingsFile));
        document.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(StorageSettings.CurrentSchemaVersion);
        document.RootElement.TryGetProperty("KVData", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("ChatMessage", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("DebugOutput", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("VectorStore", out _).Should().BeTrue();
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_IsNotOverwrittenByDefaults()
    {
        WriteSettings(kv: "LocalFileStorage", chat: "LiteDB", debug: "LiteDB", vector: "File");

        var loaded = _store.LoadOrCreate(
            StorageSettings.CreateDefaults(legacyStore: null, legacyVectorStore: null),
            out var created);

        created.Should().BeFalse();
        loaded.KVData.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        loaded.VectorStore.GetProvider().Should().Be(ShimmerChatLib.Interface.VectorStoreProvider.File);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var act = () => _store.Load();

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        WriteRaw("{ not json");

        var act = () => _store.Load();

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Load_MissingSection_Throws()
    {
        WriteRaw("""
        {
          "SchemaVersion": 1,
          "KVData": { "StorageType": "LiteDB" },
          "ChatMessage": { "StorageType": "LiteDB" },
          "DebugOutput": { "StorageType": "LiteDB" }
        }
        """);

        var act = () => _store.Load();

        act.Should().Throw<InvalidOperationException>().WithMessage("*VectorStore*");
    }

    [Fact]
    public void Load_UnknownStorageType_Throws()
    {
        WriteSettings(kv: "Sqlite", chat: "LiteDB", debug: "LiteDB", vector: "LiteDb");

        var act = () => _store.Load();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Sqlite*");
    }

    [Fact]
    public void Load_UnknownAutoMigrateFrom_Throws()
    {
        WriteRaw("""
        {
          "SchemaVersion": 1,
          "KVData": { "StorageType": "LiteDB", "AutoMigrateFrom": "Sqlite" },
          "ChatMessage": { "StorageType": "LiteDB" },
          "DebugOutput": { "StorageType": "LiteDB" },
          "VectorStore": { "Provider": "LiteDb" }
        }
        """);

        var act = () => _store.Load();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AutoMigrateFrom*Sqlite*");
    }

    [Fact]
    public void Load_FutureSchemaVersion_Throws()
    {
        WriteSettings(
            kv: "LiteDB",
            chat: "LiteDB",
            debug: "LiteDB",
            vector: "LiteDb",
            schemaVersion: StorageSettings.CurrentSchemaVersion + 1);

        var act = () => _store.Load();

        act.Should().Throw<InvalidOperationException>().WithMessage("*schemaVersion*");
    }

    [Fact]
    public void Save_LeavesNoTemporaryFile()
    {
        _store.Save(StorageSettings.CreateDefaults(null, null));

        Directory.GetFiles(Path.GetDirectoryName(_paths.StorageSettingsFile)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void CreateDefaults_CopiesLegacyGlobalConfigToEachStoreIndependently()
    {
        var legacy = new KVDataStorageConfig
        {
            StorageType = "LocalFileStorage",
            AutoMigrateOnStartup = false,
            AutoMigrateFrom = null
        };

        var defaults = StorageSettings.CreateDefaults(legacy, null);

        defaults.KVData.Should().NotBeSameAs(defaults.ChatMessage);
        defaults.ChatMessage.Should().NotBeSameAs(defaults.DebugOutput);
        defaults.KVData.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        defaults.ChatMessage.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
        defaults.DebugOutput.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);

        defaults.KVData.StorageType = "LiteDB";
        defaults.ChatMessage.GetStorageType().Should().Be(KVStorageType.LocalFileStorage);
    }

    private void WriteSettings(
        string kv,
        string chat,
        string debug,
        string vector,
        int schemaVersion = 1)
    {
        WriteRaw($$"""
        {
          "SchemaVersion": {{schemaVersion}},
          "KVData": { "StorageType": "{{kv}}", "AutoMigrateOnStartup": true, "AutoMigrateFrom": "LocalFileStorage", "ClearSourceAfterMigration": false, "ForceMigration": false },
          "ChatMessage": { "StorageType": "{{chat}}", "AutoMigrateOnStartup": true, "AutoMigrateFrom": "LocalFileStorage", "ClearSourceAfterMigration": false, "ForceMigration": false },
          "DebugOutput": { "StorageType": "{{debug}}", "AutoMigrateOnStartup": true, "AutoMigrateFrom": "LocalFileStorage", "ClearSourceAfterMigration": false, "ForceMigration": false },
          "VectorStore": { "Provider": "{{vector}}" }
        }
        """);
    }

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_paths.Root);
        File.WriteAllText(_paths.StorageSettingsFile, json);
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
