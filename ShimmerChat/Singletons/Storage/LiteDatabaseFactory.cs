using LiteDB;

namespace ShimmerChat.Singletons.Storage
{
    /// <summary>
    /// 按文件路径共享并统一释放 LiteDatabase 实例。
    /// 每个数据仓库一个文件（kvdata.db / chat_message.db / debug_output.db / vector.db / memory.db），
    /// 不再共用一个 data.db。
    /// </summary>
    public sealed class LiteDatabaseFactory : IDisposable
    {
        private readonly Dictionary<string, LiteDatabase> _databases = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private bool _disposed;

        public LiteDatabase Open(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_databases.TryGetValue(path, out var existing)) return existing;

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var database = new LiteDatabase($"Filename={path};Connection=direct");
                _databases[path] = database;
                return database;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var database in _databases.Values)
                    database.Dispose();

                _databases.Clear();
            }
        }
    }
}
