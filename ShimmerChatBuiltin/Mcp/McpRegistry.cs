using System.Text;
using Microsoft.Extensions.Logging;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// 进程级 MCP 连接注册表 —— 插件内唯一的连接真源。
    /// <para>
    /// 为什么是静态单例：插件服务由宿主通过 <c>ActivatorUtilities.CreateInstance</c> 按需创建，
    /// 没有单例注册通道；而连接池必须跨组件（聊天面板 / 前生成节点）与跨生成存活，
    /// 因此在这里持有唯一实例，不依赖主项目或共享库的任何 MCP 感知代码。
    /// </para>
    /// <para>
    /// 语义：
    /// <list type="bullet">
    /// <item><b>租约</b>：<see cref="AcquireAsync"/> 增加引用计数并返回 <see cref="McpEndpointLease"/>，
    /// 释放租约只减计数；计数归零后进入空闲计时，超时才真正关闭连接 —— 避免每轮生成都重连。</item>
    /// <item><b>配置驱动</b>：连接指纹包含全部传输相关字段；配置变化时自动丢弃旧连接重建，
    /// 暴露策略（勾选工具 / 日志级别）变化不会触发重连。</item>
    /// <item><b>失败负缓存</b>：连接失败会在退避窗口内直接回报同一次失败，
    /// 不再让每个调用方都等满握手超时。</item>
    /// <item><b>失效</b>：<see cref="Invalidate"/> 用于设置页保存后强制下次重建。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 生命周期：进程退出时通过 <see cref="AppDomain.ProcessExit"/> 尽力关闭全部连接；
    /// 正常退出路径可显式调用 <see cref="ShutdownAsync"/>。
    /// </para>
    /// </summary>
    public sealed class McpRegistry
    {
        /// <summary>全局唯一实例。</summary>
        public static McpRegistry Instance { get; } = new();

        /// <summary>引用计数归零后连接的保活时长。</summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

        /// <summary>连接失败后的负缓存窗口。</summary>
        public static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FailureRecord> _failures = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        private ILogger? _logger;

        static McpRegistry()
        {
            // 进程退出时尽力关闭子进程，避免留下孤儿。
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Instance.ShutdownBlocking();
        }

        /// <summary>当前持有连接的端点数（诊断用）。</summary>
        public int ConnectedEndpointCount
        {
            get
            {
                lock (_sync)
                    return _entries.Values.Count(e => e.Session != null);
            }
        }

        /// <summary>当前被租约占用的端点数（诊断用）。</summary>
        public int LeasedEndpointCount
        {
            get
            {
                lock (_sync)
                    return _entries.Values.Count(e => e.RefCount > 0);
            }
        }

        /// <summary>
        /// 取得该端点的一条连接租约。断开/无法连接时抛出 <see cref="McpConnectException"/>，
        /// 配置非法时抛出 <see cref="McpConfigException"/>（两者都不计入负缓存）。
        /// </summary>
        public async Task<McpEndpointLease> AcquireAsync(
            McpEndpointConfig config, ILoggerFactory? loggerFactory, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (loggerFactory != null && _logger == null)
                _logger = loggerFactory.CreateLogger("ShimmerChat.Mcp.Registry");

            var endpointId = config.Key;
            ThrowIfRecentlyFailed(endpointId);

            var gate = GetGate(endpointId);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var fingerprint = ComputeFingerprint(config);
                var stale = DetachStaleSession(endpointId, fingerprint);
                if (stale != null)
                {
                    _logger?.LogInformation(
                        "[MCP:{Endpoint}] connection configuration changed; reconnecting.", endpointId);
                    await stale.DisposeAsync().ConfigureAwait(false);
                }

                if (!TryGetSession(endpointId, out var session))
                {
                    session = await ConnectAsync(config, loggerFactory, endpointId, fingerprint, ct).ConfigureAwait(false);
                }

                lock (_sync)
                {
                    var entry = _entries[endpointId];
                    entry.RefCount++;
                    CancelIdleTimer(entry);

                    return new McpEndpointLease(this, entry, config, entry.Session!);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 失效指定端点：关闭空闲连接并清掉负缓存；若仍被租约占用则标记为待重建，
        /// 等租约全部释放后自然淘汰，避免打断正在执行的工具调用。
        /// </summary>
        public void Invalidate(string endpointId)
        {
            McpEndpointSession? toClose = null;

            lock (_sync)
            {
                _failures.Remove(endpointId);
                if (!_entries.TryGetValue(endpointId, out var entry))
                    return;

                CancelIdleTimer(entry);
                entry.Stale = true;

                if (entry.RefCount == 0)
                {
                    toClose = entry.Session;
                    entry.Session = null;
                    entry.Stale = false;
                }
            }

            if (toClose != null)
                _ = CloseQuietlyAsync(toClose, endpointId);
        }

        /// <summary>失效全部端点。设置页保存/删除端点后调用。</summary>
        public void InvalidateAll()
        {
            List<string> ids;
            lock (_sync)
            {
                ids = _entries.Keys.ToList();
            }

            foreach (var id in ids)
                Invalidate(id);

            lock (_sync)
            {
                _failures.Clear();
            }
        }

        /// <summary>关闭全部连接并清空状态。宿主退出或测试用。</summary>
        public async Task ShutdownAsync()
        {
            List<McpEndpointSession> sessions;

            lock (_sync)
            {
                sessions = _entries.Values
                    .Select(e => e.Session)
                    .Where(s => s != null)
                    .Select(s => s!)
                    .ToList();

                foreach (var entry in _entries.Values)
                {
                    CancelIdleTimer(entry);
                    entry.Session = null;
                    entry.RefCount = 0;
                    entry.Stale = false;
                }

                _entries.Clear();
                _failures.Clear();
            }

            foreach (var session in sessions)
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 关闭失败不能抛出（调用方通常已处于收尾流程），但必须留痕。
                    _logger?.LogWarning(ex, "[MCP] shutdown failed to dispose a session: {Message}", ex.Message);
                }
            }
        }

        /// <summary>进程退出路径的同步收尾（ProcessExit 不允许 await）。</summary>
        internal void ShutdownBlocking()
        {
            try
            {
                ShutdownAsync().Wait(ShutdownTimeout);
            }
            catch (Exception ex)
            {
                // 进程正在退出：没有更合适的上报通道，只尽力而为。
                _logger?.LogWarning(ex, "[MCP] shutdown on process exit failed: {Message}", ex.Message);
            }
        }

        /// <summary>清空内部状态但不触碰已有连接，仅供测试隔离使用。</summary>
        internal void ResetForTests()
        {
            lock (_sync)
            {
                _entries.Clear();
                _failures.Clear();
            }
        }

        // ---- lease release ----

        internal async ValueTask ReleaseAsync(Entry entry)
        {
            CancellationTokenSource? idleCts = null;

            lock (_sync)
            {
                if (entry.RefCount > 0)
                    entry.RefCount--;

                if (entry.RefCount == 0 && entry.IdleCts == null)
                {
                    idleCts = new CancellationTokenSource();
                    entry.IdleCts = idleCts;
                }
            }

            if (idleCts == null)
                return;

            // 空闲回收是后台行为：不阻塞租约释放，异常必须自己收口。
            await Task.CompletedTask.ConfigureAwait(false);
            _ = CloseWhenIdleAsync(entry, idleCts);
        }

        private async Task CloseWhenIdleAsync(Entry entry, CancellationTokenSource idleCts)
        {
            try
            {
                await Task.Delay(IdleTimeout, idleCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 保活期结束前又被租用：直接返回。
                return;
            }

            McpEndpointSession? toClose = null;

            lock (_sync)
            {
                if (!ReferenceEquals(entry.IdleCts, idleCts))
                    return;

                entry.IdleCts = null;
                if (entry.RefCount != 0)
                    return;

                toClose = entry.Session;
                entry.Session = null;
                entry.Stale = false;
            }

            idleCts.Dispose();

            if (toClose == null)
                return;

            _logger?.LogDebug("[MCP:{Endpoint}] idle timeout; connection closed.", entry.EndpointId);
            await CloseQuietlyAsync(toClose, entry.EndpointId).ConfigureAwait(false);
        }

        // ---- internals ----

        private SemaphoreSlim GetGate(string endpointId)
        {
            lock (_sync)
            {
                if (!_gates.TryGetValue(endpointId, out var gate))
                {
                    gate = new SemaphoreSlim(1, 1);
                    _gates[endpointId] = gate;
                }

                return gate;
            }
        }

        private McpEndpointSession? DetachStaleSession(string endpointId, string fingerprint)
        {
            lock (_sync)
            {
                var entry = GetOrAddEntry(endpointId);
                var stale = entry.Stale
                    || (entry.Session != null && !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal));

                if (!stale)
                    return null;

                var session = entry.Session;
                entry.Session = null;
                entry.Stale = false;
                return session;
            }
        }

        private bool TryGetSession(string endpointId, out McpEndpointSession session)
        {
            lock (_sync)
            {
                var entry = GetOrAddEntry(endpointId);
                if (entry.Session != null)
                {
                    session = entry.Session;
                    return true;
                }
            }

            session = null!;
            return false;
        }

        private async Task<McpEndpointSession> ConnectAsync(
            McpEndpointConfig config, ILoggerFactory? loggerFactory,
            string endpointId, string fingerprint, CancellationToken ct)
        {
            McpEndpointSession session;
            try
            {
                session = await McpEndpointSession.ConnectAsync(config, loggerFactory, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (McpConfigException)
            {
                // 配置非法是本地可判定的错误，重试没有意义，不计入负缓存。
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(endpointId, ex);
                throw;
            }

            lock (_sync)
            {
                var entry = GetOrAddEntry(endpointId);
                entry.Session ??= session;
                entry.Fingerprint = fingerprint;
                _failures.Remove(endpointId);
            }

            return session;
        }

        private Entry GetOrAddEntry(string endpointId)
        {
            if (!_entries.TryGetValue(endpointId, out var entry))
            {
                entry = new Entry(endpointId);
                _entries[endpointId] = entry;
            }

            return entry;
        }

        private static void CancelIdleTimer(Entry entry)
        {
            entry.IdleCts?.Cancel();
            entry.IdleCts?.Dispose();
            entry.IdleCts = null;
        }

        private void ThrowIfRecentlyFailed(string endpointId)
        {
            FailureRecord? record;
            lock (_sync)
            {
                if (!_failures.TryGetValue(endpointId, out record))
                    return;

                if (record.ExpiresUtc <= DateTime.UtcNow)
                {
                    _failures.Remove(endpointId);
                    return;
                }
            }

            var remaining = Math.Max(0, (int)(record.ExpiresUtc - DateTime.UtcNow).TotalSeconds);
            throw new McpConnectException(
                $"MCP endpoint '{endpointId}' failed to connect recently and is in a {remaining}s backoff window. " +
                $"Original error: {record.Message}");
        }

        private void RecordFailure(string endpointId, Exception error)
        {
            lock (_sync)
            {
                _failures[endpointId] = new FailureRecord(
                    DateTime.UtcNow + FailureBackoff,
                    error.Message);
            }
        }

        private async Task CloseQuietlyAsync(McpEndpointSession session, string endpointId)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[MCP:{Endpoint}] failed to dispose a detached session: {Message}", endpointId, ex.Message);
            }
        }

        /// <summary>
        /// 连接指纹：只包含影响连接本身的字段。
        /// 暴露策略（工具勾选 / 资源开关 / 日志级别）变化不应导致重连。
        /// </summary>
        internal static string ComputeFingerprint(McpEndpointConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(config.Transport).Append('\n');

            switch (config.Transport)
            {
                case McpTransportType.Stdio:
                    sb.Append(config.Command).Append('\n');
                    AppendLines(sb, config.Arguments);
                    AppendLines(sb, config.Environment);
                    sb.Append(config.InheritEnvironment).Append('\n');
                    sb.Append(config.WorkingDirectory).Append('\n');
                    break;

                case McpTransportType.Http:
                    sb.Append(config.Url).Append('\n');
                    sb.Append(config.HttpMode).Append('\n');
                    AppendLines(sb, config.Headers);
                    break;
            }

            sb.Append(config.InitializationTimeoutSeconds);
            return sb.ToString();
        }

        private static void AppendLines(StringBuilder sb, IEnumerable<string> lines)
        {
            foreach (var line in lines)
                sb.Append(line).Append('\u001f');

            sb.Append('\n');
        }

        internal sealed class Entry(string endpointId)
        {
            public string EndpointId { get; } = endpointId;
            public string Fingerprint { get; set; } = "";
            public McpEndpointSession? Session { get; set; }
            public int RefCount { get; set; }
            public CancellationTokenSource? IdleCts { get; set; }

            /// <summary>已被 <see cref="Invalidate"/> 标记，下次取用时丢弃。</summary>
            public bool Stale { get; set; }
        }

        private sealed record FailureRecord(DateTime ExpiresUtc, string Message);
    }

    /// <summary>
    /// 一条连接租约。<see cref="DisposeAsync"/> 只减少引用计数，不会立即关闭连接
    /// （由 <see cref="McpRegistry.IdleTimeout"/> 的空闲回收负责）。
    /// </summary>
    public sealed class McpEndpointLease : IAsyncDisposable
    {
        private readonly McpRegistry _registry;
        private readonly McpRegistry.Entry _entry;
        private bool _released;

        internal McpEndpointLease(
            McpRegistry registry, McpRegistry.Entry entry, McpEndpointConfig config, McpEndpointSession session)
        {
            _registry = registry;
            _entry = entry;
            Config = config;
            Session = session;
        }

        /// <summary>取用时的配置快照。</summary>
        public McpEndpointConfig Config { get; }

        /// <summary>已连接的会话（工具 / 资源 / 提示词缓存在会话内）。</summary>
        public McpEndpointSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            await _registry.ReleaseAsync(_entry).ConfigureAwait(false);
        }
    }
}
