using System.Text;
using Microsoft.Extensions.Logging;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// 一次生成（或一个面板生命周期）内共享的 MCP 会话集合。
    /// 同一 EndpointId 只会建立一条连接，避免工具节点与资源节点重复拉起进程/重复握手。
    /// </summary>
    public sealed class McpSessionScope : IAsyncDisposable
    {
        private readonly IReadOnlyList<McpEndpointConfig> _endpoints;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly Dictionary<string, McpEndpointSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _gate = new(1, 1);
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
        public int ActiveSessionCount => _sessions.Count;

        /// <summary>按 Id 查找启用的端点。未找到或已禁用时返回 null。</summary>
        public McpEndpointConfig? FindEnabled(string endpointId) =>
            _endpoints.FirstOrDefault(e => e.Enabled && string.Equals(e.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        /// <summary>建立（或复用）指定端点的会话。连接失败抛出 <see cref="McpConnectException"/>。</summary>
        public async Task<McpEndpointSession> EnsureConnectedAsync(string endpointId, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_sessions.TryGetValue(endpointId, out var existing))
                {
                    SessionConnected?.Invoke(endpointId, false);
                    return existing;
                }

                var config = FindEnabled(endpointId)
                    ?? throw new McpConfigException($"MCP endpoint '{endpointId}' was not found or is disabled.");

                var session = await McpEndpointSession.ConnectAsync(config, _loggerFactory, ct).ConfigureAwait(false);
                _sessions[endpointId] = session;
                SessionConnected?.Invoke(endpointId, true);
                return session;
            }
            finally
            {
                _gate.Release();
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
            if (_disposed) return;
            _disposed = true;

            var sessions = _sessions.Values.ToList();
            _sessions.Clear();

            foreach (var session in sessions)
                await session.DisposeAsync().ConfigureAwait(false);

            _gate.Dispose();
        }
    }
}
