using System.Collections.Concurrent;

namespace ShimmerChatBuiltin.SubAgent
{
    public static class SubAgentResultStore
    {
        private static readonly ConcurrentDictionary<string, Task<string>> _results = new();

        public static void Put(string id, Task<string> task)
        {
            _results[id] = task;
        }

        public static string? Take(string id)
        {
            if (_results.TryRemove(id, out var task))
            {
                try
                {
                    return task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    return $"[Error: {ex.Message}]";
                }
            }
            return null;
        }

        public static void Clear()
        {
            _results.Clear();
        }
    }
}
