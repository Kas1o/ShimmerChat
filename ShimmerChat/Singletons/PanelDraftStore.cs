using System.Collections.Concurrent;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// <see cref="IPanelDraftStore"/> 的 Scoped 实现：每个 Blazor 回路一份草稿，
    /// 面板折叠或重新渲染时草稿不丢失，用户切换对话也互不干扰（键由面板决定）。
    /// </summary>
    public class PanelDraftStore : IPanelDraftStore
    {
        private readonly ConcurrentDictionary<string, string> _drafts = new(StringComparer.Ordinal);

        public int Count => _drafts.Count;

        public string this[string key]
        {
            get => _drafts.TryGetValue(key, out var value) ? value : "";
            set
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                _drafts[key] = value ?? "";
            }
        }

        public string GetOrCreate(string key, Func<string> factory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(factory);
            return _drafts.GetOrAdd(key, _ => factory() ?? "");
        }

        public void Remove(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _drafts.TryRemove(key, out _);
        }
    }
}
