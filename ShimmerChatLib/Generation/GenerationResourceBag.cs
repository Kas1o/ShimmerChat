namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 生成作用域的资源袋。供节点注册需要跨「一次生成」存活、并在生成结束时释放的资源
    /// （例如 MCP 子进程连接、HTTP 会话、临时文件句柄）。
    /// <para>
    /// 与 <see cref="PersistentEnv"/> 绑定：Tool Call 循环重建 <see cref="TransientEnv"/> 时
    /// 本对象保持不变，因此同一次生成内的所有节点共享同一份资源。
    /// 宿主（<c>GenerationManagerV2</c>）在生成结束时调用 <see cref="DisposeAsync"/>。
    /// </para>
    /// </summary>
    public sealed class GenerationResourceBag : IAsyncDisposable
    {
        private readonly Dictionary<string, object> _resources = new(StringComparer.Ordinal);
        private readonly List<IAsyncDisposable> _extraDisposables = [];
        private readonly object _gate = new();
        private bool _disposed;

        /// <summary>当前登记的命名资源数量。</summary>
        public int Count
        {
            get { lock (_gate) return _resources.Count + _extraDisposables.Count; }
        }

        /// <summary>
        /// 按键取得资源；不存在时调用 <paramref name="factory"/> 创建并登记。
        /// 同一键的并发调用只会创建一次。
        /// </summary>
        public T GetOrAdd<T>(string key, Func<T> factory) where T : class
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(factory);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_resources.TryGetValue(key, out var existing))
                {
                    if (existing is not T typed)
                        throw new InvalidOperationException(
                            $"Generation resource '{key}' is registered as {existing.GetType().FullName}, not {typeof(T).FullName}.");

                    return typed;
                }

                var created = factory()
                    ?? throw new InvalidOperationException($"Generation resource factory for '{key}' returned null.");

                _resources[key] = created;
                return created;
            }
        }

        /// <summary>登记一个需要在生成结束时释放的对象（同一实例重复登记只保留一份）。</summary>
        public void AddDisposable(IAsyncDisposable disposable)
        {
            ArgumentNullException.ThrowIfNull(disposable);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_extraDisposables.Contains(disposable))
                    _extraDisposables.Add(disposable);
            }
        }

        /// <summary>当前是否已登记指定键的资源。</summary>
        public bool Contains(string key)
        {
            lock (_gate) return _resources.ContainsKey(key);
        }

        public async ValueTask DisposeAsync()
        {
            List<object> resources;
            List<IAsyncDisposable> extras;

            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;

                resources = _resources.Values.ToList();
                extras = _extraDisposables.ToList();
                _resources.Clear();
                _extraDisposables.Clear();
            }

            List<Exception>? errors = null;
            foreach (var resource in resources.Cast<object>().Concat(extras))
            {
                // 命名为资源的对象可能实现了 IAsyncDisposable 或 IDisposable；
                // 两者都不实现时说明登记有误，报告而不是忽略。
                try
                {
                    switch (resource)
                    {
                        case IAsyncDisposable asyncDisposable:
                            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                            break;
                        case IDisposable disposable:
                            disposable.Dispose();
                            break;
                        default:
                            (errors ??= []).Add(new InvalidOperationException(
                                $"Generation resource of type {resource.GetType().FullName} is neither IAsyncDisposable nor IDisposable."));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            if (errors != null)
                throw new AggregateException("One or more generation-scoped resources failed to dispose.", errors);
        }
    }
}
