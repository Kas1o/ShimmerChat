using Microsoft.Extensions.Logging;

namespace ShimmerChatBuiltin.Memory
{
    /// <summary>
    /// 进程级同步任务宿主。面板是瞬态组件，任务必须活在这里，
    /// 否则切换页面就会中断构建。
    /// </summary>
    public static class MemorySyncCoordinator
    {
        private static readonly object Sync = new();
        private static CancellationTokenSource? _cts;
        private static Task? _job;

        public static event Action? Changed;

        public static MemorySyncProgress? Progress { get; private set; }

        public static string? Error { get; private set; }

        /// <summary>任务是否因用户取消而结束；UI 据此显示本地化文案，而不是把取消当错误。</summary>
        public static bool Cancelled { get; private set; }

        public static bool IsRunning
        {
            get
            {
                lock (Sync) return _job is { IsCompleted: false };
            }
        }

        public static bool Start(
            Func<CancellationToken, IProgress<MemorySyncProgress>, Task> work,
            ILogger? logger = null)
        {
            lock (Sync)
            {
                if (_job is { IsCompleted: false }) return false;

                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                Progress = new MemorySyncProgress(0, 0, 0, "starting");
                Error = null;
                Cancelled = false;

                var token = _cts.Token;
                var progress = new Progress<MemorySyncProgress>(value =>
                {
                    Progress = value;
                    Changed?.Invoke();
                });

                _job = Task.Run(async () =>
                {
                    try
                    {
                        await work(token, progress).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Cancelled = true;
                        logger?.LogInformation("记忆索引构建任务已取消。");
                    }
                    catch (Exception ex)
                    {
                        Error = ex.Message;
                        logger?.LogError(ex, "记忆索引构建任务失败：{Message}", ex.Message);
                    }
                    finally
                    {
                        Changed?.Invoke();
                    }
                }, token);

                return true;
            }
        }

        public static void Cancel()
        {
            lock (Sync) _cts?.Cancel();
        }

        /// <summary>重置已完成的任务状态，供 UI 收起结果。</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                if (_job is { IsCompleted: false }) return;
                _job = null;
                Progress = null;
                Error = null;
                Cancelled = false;
                Changed?.Invoke();
            }
        }
    }
}
