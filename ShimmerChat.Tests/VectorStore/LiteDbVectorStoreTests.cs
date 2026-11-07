using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChat.Tests.VectorStore;

public sealed class LiteDbVectorStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LiteDbVectorStore _store;
    private bool _disposed;

    public LiteDbVectorStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shimmer-vector-tests", Guid.NewGuid().ToString("N"));
        _store = CreateStore(_root);
    }

    private static LiteDbVectorStore CreateStore(string root)
    {
        return new LiteDbVectorStore(
            new VectorStoreConfig { Provider = "LiteDb", RootPath = root, Scope = "PerOwner" },
            NullLogger<LiteDbVectorStore>.Instance);
    }

    private static float[] Vector(params float[] values) => values;

    private static VectorRecord Record(
        string id,
        float[] vector,
        string? partition = null,
        DateTimeOffset? createdAt = null,
        IReadOnlyDictionary<string, string>? payload = null)
    {
        return new VectorRecord(id, vector, partition, createdAt ?? DateTimeOffset.UtcNow, payload);
    }

    [Fact]
    public async Task EnsureCollection_ThenList_ReturnsMetadata()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3, VectorMetric.Cosine, "test-model"));

        var collections = await _store.ListCollectionsAsync();

        collections.Should().ContainSingle();
        collections[0].Name.Should().Be("memory");
        collections[0].Options.Dimensions.Should().Be(3);
        collections[0].Options.Metric.Should().Be(VectorMetric.Cosine);
        collections[0].Options.EmbeddingModel.Should().Be("test-model");
        collections[0].Count.Should().Be(0);
    }

    [Fact]
    public async Task EnsureCollection_SameOptionsTwice_IsIdempotent()
    {
        var options = new CollectionOptions(3, VectorMetric.Cosine);

        await _store.EnsureCollectionAsync("memory", options);
        await _store.EnsureCollectionAsync("memory", options);

        (await _store.ListCollectionsAsync()).Should().ContainSingle();
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
    public async Task Upsert_WrongDimension_Throws()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));

        var act = () => _store.UpsertAsync("memory", [Record("a", Vector(1, 0))]);

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.DimensionMismatch);
    }

    [Fact]
    public async Task Search_ReturnsNearestFirst()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("x", Vector(1, 0, 0)),
            Record("y", Vector(0.9f, 0.1f, 0)),
            Record("z", Vector(0, 1, 0))
        ]);

        var hits = await _store.SearchAsync("memory", Vector(1, 0, 0), topK: 2);

        hits.Should().HaveCount(2);
        hits[0].Id.Should().Be("x");
        hits[1].Id.Should().Be("y");
        hits[0].Score.Should().BeGreaterThan(hits[1].Score);
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
    public async Task Search_TagFilter_RequiresAllTags()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("both", Vector(1, 0, 0), payload: new Dictionary<string, string> { ["tags"] = "x,y" }),
            Record("onlyX", Vector(1, 0, 0), payload: new Dictionary<string, string> { ["tags"] = "x" })
        ]);

        var hits = await _store.SearchAsync(
            "memory", Vector(1, 0, 0), topK: 10, filter: new VectorFilter(Tags: ["x", "y"]));

        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("both");
    }

    [Fact]
    public async Task Search_RespectsMinScore()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("near", Vector(1, 0, 0)),
            Record("far", Vector(0, 1, 0))
        ]);

        var hits = await _store.SearchAsync("memory", Vector(1, 0, 0), topK: 10, minScore: 0.5f);

        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("near");
    }

    [Fact]
    public async Task SearchBatch_ReturnsOneResultPerQuery()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("x", Vector(1, 0, 0)),
            Record("z", Vector(0, 1, 0))
        ]);

        var results = await _store.SearchBatchAsync(
            "memory", [Vector(1, 0, 0), Vector(0, 1, 0)], topK: 1);

        results.Should().HaveCount(2);
        results[0].Single().Id.Should().Be("x");
        results[1].Single().Id.Should().Be("z");
    }

    [Fact]
    public async Task Delete_ByPartition_RemovesOnlyThatPartition()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory",
        [
            Record("a", Vector(1, 0, 0), partition: "agent-1"),
            Record("b", Vector(1, 0, 0), partition: "agent-2")
        ]);

        await _store.DeleteAsync("memory", new VectorFilter(Partition: "agent-2"));

        var collections = await _store.ListCollectionsAsync();
        collections.Single().Count.Should().Be(1);

        var hits = await _store.SearchAsync("memory", Vector(1, 0, 0), topK: 10);
        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("a");
    }

    [Fact]
    public async Task Delete_EmptyFilter_Throws()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));

        var act = () => _store.DeleteAsync("memory", new VectorFilter());

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task DropCollection_RemovesItFromList()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory", [Record("a", Vector(1, 0, 0))]);

        await _store.DropCollectionAsync("memory");

        (await _store.ListCollectionsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Persistence_Reopen_FindsExistingVectors()
    {
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));
        await _store.UpsertAsync("memory", [Record("a", Vector(1, 0, 0), partition: "p1")]);

        await _store.DisposeAsync();
        _disposed = true;

        await using var reopened = CreateStore(_root);
        var collections = await reopened.ListCollectionsAsync();
        collections.Should().ContainSingle();
        collections[0].Count.Should().Be(1);

        var hits = await reopened.SearchAsync("memory", Vector(1, 0, 0), topK: 1);
        hits.Single().Id.Should().Be("a");
    }

    [Fact]
    public async Task PluginCollections_UseSeparateFilesPerOwner()
    {
        await _store.EnsureCollectionAsync("plugin:alpha:notes", new CollectionOptions(3));
        await _store.EnsureCollectionAsync("plugin:beta:notes", new CollectionOptions(3));
        await _store.EnsureCollectionAsync("memory", new CollectionOptions(3));

        var files = Directory.GetFiles(_root, "*.db")
            .Select(Path.GetFileName)
            .Where(name => !name!.EndsWith("-log.db", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        files.Should().HaveCount(3);
        files.Should().Contain("default.db");
        files.Should().Contain(name => name!.StartsWith("plugin_alpha-"));
        files.Should().Contain(name => name!.StartsWith("plugin_beta-"));
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy()
    {
        var health = await _store.CheckHealthAsync();

        health.Healthy.Should().BeTrue();
        health.ProviderId.Should().Be("LiteDb");
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
