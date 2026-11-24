using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;

namespace ShimmerChat.Tests.VectorStore;

/// <summary>
/// 文件后端：每个集合一个 JSON 文件，写入后重开仍可检索；错误码与 LiteDb 后端保持一致。
/// </summary>
public sealed class FileVectorStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileVectorStore _store;
    private bool _disposed;

    public FileVectorStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shimmer-file-vector-tests", Guid.NewGuid().ToString("N"));
        _store = CreateStore(_root);
    }

    private static FileVectorStore CreateStore(string root)
        => new(new DataPaths(root), NullLogger<FileVectorStore>.Instance);

    private static float[] Vector(params float[] values) => values;

    private static VectorRecord Record(
        string id,
        float[] vector,
        string? partition = null,
        IReadOnlyDictionary<string, string>? payload = null)
        => new(id, vector, partition, DateTimeOffset.UtcNow, payload);

    [Fact]
    public async Task EnsureCollection_WritesOneJsonFilePerCollection()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.EnsureCollectionAsync("plugin:alpha:notes", new CollectionOptions(3));

        var files = Directory.GetFiles(new DataPaths(_root).LocalVectorDirectory, "*.json");

        files.Should().HaveCount(2);
        (await _store.ListCollectionsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Persistence_Reopen_FindsExistingVectors()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3, VectorMetric.Cosine, "test-model"));
        await _store.UpsertAsync("memory",
        [
            Record("a", Vector(1, 0, 0), partition: "p1", payload: new Dictionary<string, string> { ["tags"] = "x" })
        ]);

        await _store.DisposeAsync();
        _disposed = true;

        await using var reopened = CreateStore(_root);

        var collections = await reopened.ListCollectionsAsync();
        collections.Should().ContainSingle();
        collections[0].Name.Should().Be("memory");
        collections[0].Options.Dimensions.Should().Be(3);
        collections[0].Options.EmbeddingModel.Should().Be("test-model");
        collections[0].Count.Should().Be(1);

        var hits = await reopened.SearchAsync("memory", Vector(1, 0, 0), topK: 1);
        hits.Single().Id.Should().Be("a");
        hits.Single().Payload!["tags"].Should().Be("x");
    }

    [Fact]
    public async Task Search_PartitionFilter_OnlyReturnsThatPartition()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("a", Vector(1, 0, 0), partition: "agent-1"),
            Record("b", Vector(1, 0, 0), partition: "agent-2")
        ]);

        var hits = await _store.SearchAsync(
            "memory", Vector(1, 0, 0), topK: 10, filter: new VectorFilter(Partition: "agent-1"));

        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("a");
    }

    [Fact]
    public async Task Delete_ByPartition_PersistsAcrossReopen()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("a", Vector(1, 0, 0), partition: "agent-1"),
            Record("b", Vector(1, 0, 0), partition: "agent-2")
        ]);

        await _store.DeleteAsync("memory", new VectorFilter(Partition: "agent-2"));

        await _store.DisposeAsync();
        _disposed = true;

        await using var reopened = CreateStore(_root);
        var records = await reopened.ListRecordsAsync("memory");
        records.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public async Task DropCollection_RemovesFileAndListEntry()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory", [Record("a", Vector(1, 0, 0))]);

        await _store.DropCollectionAsync("memory");

        (await _store.ListCollectionsAsync()).Should().BeEmpty();
        Directory.GetFiles(new DataPaths(_root).LocalVectorDirectory, "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureCollection_DimensionMismatch_Throws()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));

        var act = () => _store.EnsureCollectionAsync("memory", new CollectionOptions(4));

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.DimensionMismatch);
    }

    [Fact]
    public async Task Upsert_WithoutEnsure_Throws()
    {
        var act = () => _store.UpsertAsync("memory", [Record("a", Vector(1, 0, 0))]);

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.CollectionNotFound);
    }

    [Fact]
    public async Task Search_RespectsMinScoreAndTagFilter()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("near", Vector(1, 0, 0), payload: new Dictionary<string, string> { ["tags"] = "x,y" }),
            Record("far", Vector(0, 1, 0), payload: new Dictionary<string, string> { ["tags"] = "x" })
        ]);

        var hits = await _store.SearchAsync(
            "memory", Vector(1, 0, 0), topK: 10, filter: new VectorFilter(Tags: ["x", "y"]), minScore: 0.5f);

        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("near");
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy()
    {
        var health = await _store.CheckHealthAsync();

        health.Healthy.Should().BeTrue();
        health.ProviderId.Should().Be("File");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响断言结果
        }
    }
}
