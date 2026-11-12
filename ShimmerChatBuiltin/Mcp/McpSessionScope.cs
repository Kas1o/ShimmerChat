using System.Text;
using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// 一次生成（或一个面板生命周期）内共享的 MCP 会话集合。
    /// 同一 EndpointId 只会建立一条连接，避免工具节点与资源节点重复拉起进程/重复握手。
    /// <para>
    /// 并发语义：不同端点的连接<b>可以并行</b>（各自一把端点级锁）；
    /// 同一端点的并发连接会被合并（共用同一把锁，后续调用直接拿到已建立的会话）。
    /// </para>
    /// </summary>
    public sealed class McpSessionScope : IAsyncDisposable
    {
        private readonly IReadOnlyList<McpEndpointConfig> _endpoints;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly Dictionary<string, McpEndpointSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>每个端点一把锁：同一端点串行化连接，不同端点互不阻塞。</summary>
        private readonly Dictionary<string, SemaphoreSlim> _endpointGates = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private bool _disposed;

        /// <summary>每次建立或复用会话时触发（endpointId, isNewConnection）。</summary>
        public event Action<string, bool>? SessionConnected;

        public McpSessionScope(
            IReadOnlyList<McpEndpointConfig> endpoints,
            ILoggerFactory? loggerFactory = null)
        {
            _endpoints = endpoints;
            _loggerFactory = loggerFactory;
        }

        /// <summary>从 KVData 读取端点目录并构建作用域。</summary>
        public static McpSessionScope FromCatalog(ShimmerChatLib.Interface.IKVDataService kvData, ILoggerFactory? loggerFactory = null) =>
            new(McpEndpointCatalog.Load(kvData), loggerFactory);

        /// <summary>端点目录快照。</summary>
        public IReadOnlyList<McpEndpointConfig> Endpoints => _endpoints;

        /// <summary>已建立的会话数。</summary>
        public int ActiveSessionCount
        {
            get { lock (_sync) return _sessions.Count; }
        }

        /// <summary>按 Id 查找启用的端点。未找到或已禁用时返回 null。</summary>
        public McpEndpointConfig? FindEnabled(string endpointId) =>
            _endpoints.FirstOrDefault(e => e.Enabled && string.Equals(e.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        /// <summary>建立（或复用）指定端点的会话。连接失败抛出 <see cref="McpConnectException"/>。</summary>
        public async Task<McpEndpointSession> EnsureConnectedAsync(string endpointId, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 快速路径：已有会话时不取锁，避免面板重渲染时排到正在建立的连接后面。
            lock (_sync)
            {
                if (_sessions.TryGetValue(endpointId, out var cached))
                {
                    SessionConnected?.Invoke(endpointId, false);
                    return cached;
                }
            }

            var gate = GetEndpointGate(endpointId);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // 双重检查：等锁期间可能已被另一个调用建立。
                lock (_sync)
                {
                    if (_sessions.TryGetValue(endpointId, out var existing))
                    {
                        SessionConnected?.Invoke(endpointId, false);
                        return existing;
                    }
                }

                var config = FindEnabled(endpointId)
                    ?? throw new McpConfigException($"MCP endpoint '{endpointId}' was not found or is disabled.");

                var session = await McpEndpointSession.ConnectAsync(config, _loggerFactory, ct).ConfigureAwait(false);

                lock (_sync) _sessions[endpointId] = session;
                SessionConnected?.Invoke(endpointId, true);
                return session;
            }
            finally
            {
                gate.Release();
            }
        }

        private SemaphoreSlim GetEndpointGate(string endpointId)
        {
            lock (_sync)
            {
                if (!_endpointGates.TryGetValue(endpointId, out var gate))
                {
                    gate = new SemaphoreSlim(1, 1);
                    _endpointGates[endpointId] = gate;
                }
                return gate;
            }
        }

        /// <summary>批量建立会话；连接失败的端点以异常形式收集，由调用方决定呈现方式。</summary>
        public async Task<List<(McpEndpointConfig Endpoint, Exception Error)>> EnsureAllConnectedAsync(
            IEnumerable<McpEndpointConfig> endpoints, CancellationToken ct = default)
        {
            var failures = new List<(McpEndpointConfig, Exception)>();

            foreach (var endpoint in endpoints)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await EnsureConnectedAsync(endpoint.Id, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggerFactory?.CreateLogger("ShimmerChat.Mcp")
                        .LogWarning(ex, "[MCP] endpoint {Endpoint} unavailable: {Message}", endpoint.Id, ex.Message);
                    failures.Add((endpoint, ex));
                }
            }

            return failures;
        }

        public async ValueTask DisposeAsync()
        {
            List<McpEndpointSession> sessions;

            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;

                sessions = _sessions.Values.ToList();
                _sessions.Clear();

                // 端点锁不 Dispose：并发调用可能仍持有引用并将在 finally 中 Release，
                // Dispose 会使其抛 ObjectDisposedException。SemaphoreSlim 无需 Dispose 即可回收。
                _endpointGates.Clear();
            }

            foreach (var session in sessions)
                await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
