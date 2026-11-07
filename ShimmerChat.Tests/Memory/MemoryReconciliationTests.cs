using FluentAssertions;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests.Memory;

public sealed class MemoryReconciliationTests : IDisposable
{
    private readonly string _root;
    private readonly MemoryRecordStore _store;
    private readonly InMemoryVectorStore _vectors;
    private readonly MemoryModelConfig _config;
    private readonly MemoryIndexService _service;

    public MemoryReconciliationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shimmer-memory-tests", Guid.NewGuid().ToString("N"));
        _store = new MemoryRecordStore(Path.Combine(_root, "records.db"));
        _vectors = new InMemoryVectorStore();
        _config = new MemoryModelConfig("http://localhost:1234/v1", string.Empty, "test-model");
        _service = new MemoryIndexService(_store, _vectors, _config);
    }

    private async Task<(MemoryRecord Record, string AgentId)> SeedRecordWithVectorAsync()
    {
        const string agentId = "agent-1";
        await _vectors.EnsureCollectionAsync(
            _config.CollectionName,
            new CollectionOptions(3, VectorMetric.Cosine, _config.ModelKey));

        var record = _store.Add(agentId, "hello world", string.Empty, null);
        await _vectors.UpsertAsync(_config.CollectionName,
        [
            new VectorRecord(
                record.Id,
                new float[] { 1, 0, 0 },
                agentId,
                record.UpdatedAt,
                new Dictionary<string, string>
                {
                    ["version"] = record.Version.ToString(),
                    ["tags"] = string.Empty
                })
        ]);

        return (record, agentId);
    }

    [Fact]
    public async Task Plan_WhenVectorMatches_IsEmpty()
    {
        var (record, agentId) = await SeedRecordWithVectorAsync();

        var plan = await _service.PlanAsync(agentId);

        record.Version.Should().Be(1);
        plan.Missing.Should().BeEmpty();
        plan.Stale.Should().BeEmpty();
        plan.OrphanIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_AfterTextUpdate_DetectsStale()
    {
        var (record, agentId) = await SeedRecordWithVectorAsync();

        _store.Update(record.Id, "changed text", null);

        var plan = await _service.PlanAsync(agentId);

        plan.Stale.Should().ContainSingle(x => x.Id == record.Id);
        plan.Missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_ForVectorWithoutRecord_DetectsOrphan()
    {
        await _vectors.EnsureCollectionAsync(
            _config.CollectionName,
            new CollectionOptions(3, VectorMetric.Cosine, _config.ModelKey));
        await _vectors.UpsertAsync(_config.CollectionName,
        [
            new VectorRecord("orphan-id", new float[] { 1, 0, 0 }, "agent-1", DateTimeOffset.UtcNow)
        ]);

        var plan = await _service.PlanAsync("agent-1");

        plan.OrphanIds.Should().ContainSingle().Which.Should().Be("orphan-id");
    }

    [Fact]
    public async Task Verify_WhenClean_MarksStateReady()
    {
        var (_, agentId) = await SeedRecordWithVectorAsync();
        _store.SaveState(new MemoryModelState
        {
            Id = _config.ModelKey,
            Dimensions = 3,
            Status = MemoryStateStatus.Suspect
        });

        await _service.VerifyAsync(agentId);

        var state = _store.GetState(_config.ModelKey);
        state!.Status.Should().Be(MemoryStateStatus.Ready);
        state.BuiltRevision.Should().Be(_store.Revision);
        _service.Snapshot().Should().Be(MemoryIndexStatus.Ready);
    }

    [Fact]
    public async Task Verify_WhenStale_MarksStateSuspect()
    {
        var (record, agentId) = await SeedRecordWithVectorAsync();
        _store.SaveState(new MemoryModelState
        {
            Id = _config.ModelKey,
            Dimensions = 3,
            Status = MemoryStateStatus.Ready
        });
        _store.Update(record.Id, "changed", null);

        await _service.VerifyAsync(agentId);

        _store.GetState(_config.ModelKey)!.Status.Should().Be(MemoryStateStatus.Suspect);
        _service.Snapshot().Should().Be(MemoryIndexStatus.Suspect);
    }

    [Fact]
    public void RecordStore_UpdateContent_BumpsVersionAndRevision()
    {
        var record = _store.Add("agent-1", "original", string.Empty, null);
        var revision = _store.Revision;

        var updated = _store.Update(record.Id, "changed", null);

        updated.Version.Should().Be(2);
        _store.Revision.Should().Be(revision + 1);
    }

    public void Dispose()
    {
        _store.Dispose();
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
