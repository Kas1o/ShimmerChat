using FluentAssertions;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests.VectorStore;

public sealed class InMemoryVectorStoreTests
{
    private static InMemoryVectorStore CreateStore() => new();

    private static VectorRecord Record(string id, float[] vector, string? partition = null)
        => new(id, vector, partition, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Search_ReturnsNearestFirst()
    {
        await using var store = CreateStore();
        await store.EnsureCollectionAsync("docs", new CollectionOptions(3));
        await store.UpsertAsync("docs",
        [
            Record("x", [1, 0, 0]),
            Record("y", [0.8f, 0.2f, 0]),
            Record("z", [0, 1, 0])
        ]);

        var hits = await store.SearchAsync("docs", new float[] { 1, 0, 0 }, topK: 2);

        hits.Should().HaveCount(2);
        hits[0].Id.Should().Be("x");
        hits[1].Id.Should().Be("y");
    }

    [Fact]
    public async Task Upsert_ReplacesExistingId()
    {
        await using var store = CreateStore();
        await store.EnsureCollectionAsync("docs", new CollectionOptions(3));
        await store.UpsertAsync("docs", [Record("x", [1, 0, 0])]);
        await store.UpsertAsync("docs", [Record("x", [0, 1, 0])]);

        var hits = await store.SearchAsync("docs", new float[] { 0, 1, 0 }, topK: 10);

        hits.Should().ContainSingle();
        hits[0].Id.Should().Be("x");
        hits[0].Score.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public async Task Search_WrongDimension_Throws()
    {
        await using var store = CreateStore();
        await store.EnsureCollectionAsync("docs", new CollectionOptions(3));

        var act = () => store.SearchAsync("docs", new float[] { 1, 0 }, topK: 1);

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.DimensionMismatch);
    }

    [Fact]
    public async Task Delete_EmptyFilter_Throws()
    {
        await using var store = CreateStore();
        await store.EnsureCollectionAsync("docs", new CollectionOptions(3));

        var act = () => store.DeleteAsync("docs", new VectorFilter());

        await act.Should().ThrowAsync<VectorStoreException>()
            .Where(x => x.Code == VectorStoreErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task DropCollection_RemovesIt()
    {
        await using var store = CreateStore();
        await store.EnsureCollectionAsync("docs", new CollectionOptions(3));

        await store.DropCollectionAsync("docs");

        (await store.ListCollectionsAsync()).Should().BeEmpty();
    }
}
