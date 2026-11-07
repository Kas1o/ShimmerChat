using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatBuiltin.API;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests.Memory;

/// <summary>
/// 端到端覆盖"新增记忆 → 立即可检索"：用本地假嵌入服务验证写穿与状态推进。
/// </summary>
public sealed class MemoryIndexServiceTests : IDisposable
{
    private readonly string _root;
    private readonly MemoryRecordStore _store;
    private readonly InMemoryVectorStore _vectors = new();
    private readonly FakeEmbeddingServer _server = new();
    private readonly MemoryKV _kv = new();
    private readonly MemoryModelConfig _config;

    public MemoryIndexServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shimmer-memory-service", Guid.NewGuid().ToString("N"));
        _store = new MemoryRecordStore(Path.Combine(_root, "records.db"));

        EmbeddingProfileStore.Save(_kv,
        [
            new EmbeddingProfile
            {
                Id = "test",
                Name = "test",
                BaseUrl = _server.BaseUrl,
                ModelName = "test-embedding"
            }
        ]);
        EmbeddingProfileStore.SetActive(_kv, "test");

        _config = MemoryModelConfig.Load(_kv).Config!;
    }

    [Fact]
    public async Task AddAsync_MakesRecordSearchableImmediately()
    {
        var service = new MemoryIndexService(_store, _vectors, _config);
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());

        var record = await service.AddAsync(agentId, "hello memory");

        service.Snapshot().Should().Be(MemoryIndexStatus.Ready);

        var hits = await service.SearchAsync(agentId, ["hello memory"], 5, 0f);
        hits.Should().ContainSingle();
        hits[0].Id.Should().Be(record.Id);
        hits[0].Content.Should().Be("hello memory");
    }

    [Fact]
    public async Task AddAsync_SecondRecord_KeepsIndexReady()
    {
        var service = new MemoryIndexService(_store, _vectors, _config);
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());

        await service.AddAsync(agentId, "first");
        await service.AddAsync(agentId, "second");

        service.Snapshot().Should().Be(MemoryIndexStatus.Ready);
        (await service.SearchAsync(agentId, ["second"], 5, 0f)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_KeepsIndexReady()
    {
        var service = new MemoryIndexService(_store, _vectors, _config);
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());
        var record = await service.AddAsync(agentId, "to delete");

        await service.DeleteAsync(record.Id);

        service.Snapshot().Should().Be(MemoryIndexStatus.Ready);
        (await service.SearchAsync(agentId, ["to delete"], 5, 0f)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_NewContentIsSearchable()
    {
        var service = new MemoryIndexService(_store, _vectors, _config);
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());
        var record = await service.AddAsync(agentId, "old content");

        await service.UpdateAsync(record.Id, "new content", null);

        service.Snapshot().Should().Be(MemoryIndexStatus.Ready);
        var hits = await service.SearchAsync(agentId, ["new content"], 5, 0f);
        hits.Should().ContainSingle(x => x.Id == record.Id && x.Content == "new content");
    }

    [Fact]
    public async Task UpdateAsync_EmptyContent_IsRejectedBeforeSaving()
    {
        var service = new MemoryIndexService(_store, _vectors, _config);
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());
        var record = await service.AddAsync(agentId, "keep me");

        var act = () => service.UpdateAsync(record.Id, "   ", null);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.Get(record.Id)!.Content.Should().Be("keep me");
    }

    [Fact]
    public async Task GetStatusAsync_AfterVectorStoreRestart_IsNotReady()
    {
        var agentId = MemoryPartition.ForAgent(Guid.NewGuid());
        var service = new MemoryIndexService(_store, _vectors, _config);
        await service.AddAsync(agentId, "persisted record");

        // 模拟 InMemory 后端重启：向量库空了，但文本库和索引状态仍在。
        await using var freshVectors = new InMemoryVectorStore();
        var restarted = new MemoryIndexService(_store, freshVectors, _config);

        (await restarted.GetStatusAsync(agentId)).Should().NotBe(MemoryIndexStatus.Ready);
        await FluentActions.Awaiting(() => restarted.SearchAsync(agentId, ["persisted record"], 5, 0f))
            .Should().ThrowAsync<MemoryNotReadyException>();
    }

    [Fact]
    public async Task PartialGlobalState_StillAllowsReadyAgentToSearch()
    {
        var readyAgent = MemoryPartition.ForAgent(Guid.NewGuid());
        var missingAgent = MemoryPartition.ForAgent(Guid.NewGuid());

        var service = new MemoryIndexService(_store, _vectors, _config);
        await service.AddAsync(readyAgent, "agent-a-memory");

        // 另一个 Agent 只有文本、没有向量，制造全局 Suspect/Partial 状态。
        _store.Add(missingAgent, "agent-b-memory", string.Empty, null);

        (await service.GetStatusAsync(readyAgent)).Should().Be(MemoryIndexStatus.Ready);
        (await service.GetStatusAsync(missingAgent)).Should().NotBe(MemoryIndexStatus.Ready);

        var hits = await service.SearchAsync(readyAgent, ["agent-a-memory"], 5, 0f);
        hits.Should().ContainSingle();
    }

    public void Dispose()
    {
        _server.Dispose();
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

    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _values = new();

        public string? Read(string spaceId, string key)
            => _values.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
            => _values[(spaceId, key)] = value;
    }

    /// <summary>极简 HTTP 服务：对 /embeddings 固定返回三维向量。</summary>
    private sealed class FakeEmbeddingServer : IDisposable
    {
        private const string Body = "{\"data\":[{\"index\":0,\"embedding\":[1.0,0.0,0.0]}]}";
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public FakeEmbeddingServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _loop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var request = await ReadRequestAsync(stream);
                    if (request is null) return;

                    var payload = Encoding.UTF8.GetBytes(Body);
                    var header = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/json\r\n" +
                        $"Content-Length: {payload.Length}\r\n" +
                        "Connection: close\r\n\r\n");

                    await stream.WriteAsync(header, _cts.Token);
                    await stream.WriteAsync(payload, _cts.Token);
                }
            }
            catch
            {
                // 测试服务端忽略连接异常
            }
        }

        private static async Task<string?> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            using var received = new MemoryStream();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) return null;
                received.Write(buffer, 0, read);
                headerEnd = FindHeaderEnd(received.GetBuffer(), (int)received.Length);
            }

            var bytes = received.ToArray();
            var text = Encoding.ASCII.GetString(bytes, 0, headerEnd);
            var contentLength = 0;

            foreach (var line in text.Split("\r\n"))
            {
                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
                int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
            }

            var missing = contentLength - (bytes.Length - headerEnd - 4);
            while (missing > 0)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                missing -= read;
            }

            return text;
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (var i = 0; i + 3 < length; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return i;
            }

            return -1;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 忽略关闭异常
            }

            _cts.Dispose();
        }
    }
}
