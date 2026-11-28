using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.Render;

/// <summary>
/// <see cref="RenderSequenceNode"/> 的输出缓存。
/// <para>
/// 按「节点 Id + 输入 + 语言 + 树修订号 + 渲染位置」索引一次完整的子树输出。池子按总字符数
/// 和单条上限做 LRU 淘汰，而不是满就清空——整池清空会让所有热点同时失效，
/// 反而把压力全部打回 Markdown / 高亮这些昂贵节点。
/// </para>
/// <para>
/// 缓存是进程级的，因为渲染树会在每次渲染时重新反序列化、节点实例不保证复用。
/// 流式内容由调用方通过 <see cref="RenderEnv.AllowCacheWrites"/> 禁止写入，
/// 避免每个增量都留下一个一次性 key。
/// </para>
/// </summary>
internal static class RenderSequenceCache
{
	/// <summary>单条缓存允许的最大 HTML 字符数；超过则不缓存，避免单条大表格长期占满池子。</summary>
	internal const int MaxEntryChars = 4 * 1024 * 1024;

	/// <summary>池子允许缓存的总字符数（键内容 + 输出）。</summary>
	internal const long MaxTotalChars = 32L * 1024 * 1024;

	/// <summary>池子允许的条目数，防止大量小条目无限增长。</summary>
	internal const int MaxEntries = 512;

	private static readonly object Sync = new();
	private static readonly Dictionary<Key, LinkedListNode<EntryNode>> Map = new();
	private static readonly LinkedList<EntryNode> Lru = new();
	private static long _totalChars;

	internal readonly record struct Key(
		string NodeId,
		string Input,
		string? Culture,
		RenderTreeRevision Revision,
		RenderPosition Position);

	internal sealed record CachedFailure(string Code, string Message, string? NodeId, string? NodeName);

	internal sealed class Entry
	{
		public string? Html { get; init; }
		public CachedFailure? Failure { get; init; }
	}

	private sealed class EntryNode
	{
		public required Key Key { get; init; }
		public required Entry Entry { get; init; }
		public required long Size { get; init; }
	}

	internal static bool TryGet(Key key, out Entry entry)
	{
		lock (Sync)
		{
			if (!Map.TryGetValue(key, out var node))
			{
				entry = null!;
				return false;
			}

			// 命中后移到队首：淘汰时优先丢弃最久未使用的条目。
			Lru.Remove(node);
			Lru.AddFirst(node);
			entry = node.Value.Entry;
			return true;
		}
	}

	internal static void SetHtml(Key key, string html)
	{
		if (html.Length > MaxEntryChars)
		{
			// 超大输出不缓存；同时移除可能存在的旧条目，避免继续命中过期结果。
			Remove(key);
			return;
		}

		Set(key, new Entry { Html = html }, html.Length + (long)key.Input.Length);
	}

	internal static void SetFailure(Key key, RenderNodeException exception)
	{
		var failure = new CachedFailure(
			exception.Code,
			exception.Message,
			exception.NodeId,
			exception.NodeName);

		Set(key, new Entry { Failure = failure },
			(long)key.Input.Length + (failure.Message?.Length ?? 0));
	}

	private static void Set(Key key, Entry entry, long size)
	{
		lock (Sync)
		{
			if (Map.TryGetValue(key, out var existing))
			{
				_totalChars -= existing.Value.Size;
				Lru.Remove(existing);
				Map.Remove(key);
			}

			var node = new EntryNode { Key = key, Entry = entry, Size = size };
			var linked = Lru.AddFirst(node);
			Map[key] = linked;
			_totalChars += size;

			Trim();
		}
	}

	private static void Remove(Key key)
	{
		lock (Sync)
		{
			if (!Map.TryGetValue(key, out var node)) return;

			_totalChars -= node.Value.Size;
			Lru.Remove(node);
			Map.Remove(key);
		}
	}

	private static void Trim()
	{
		while (Map.Count > MaxEntries || _totalChars > MaxTotalChars)
		{
			var last = Lru.Last;
			if (last == null) break;

			Lru.RemoveLast();
			Map.Remove(last.Value.Key);
			_totalChars -= last.Value.Size;
		}
	}

	internal static void ClearForTests()
	{
		lock (Sync)
		{
			Map.Clear();
			Lru.Clear();
			_totalChars = 0;
		}
	}

	internal static int CountForTests
	{
		get { lock (Sync) return Map.Count; }
	}

	internal static long TotalCharsForTests
	{
		get { lock (Sync) return _totalChars; }
	}
}
