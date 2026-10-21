using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.SubAgent;

/// <summary>
/// 内存版 KVData，用于会话存储测试。键是 (spaceId, key)，写入即替换。
/// </summary>
public class FakeKVDataService : IKVDataService
{
    private readonly Dictionary<(string Space, string Key), string> _data = new();
    private readonly object _lock = new();

    public string? Read(string spaceId, string key)
    {
        lock (_lock)
        {
            return _data.TryGetValue((spaceId, key), out var value) ? value : null;
        }
    }

    public void Write(string spaceId, string key, string value)
    {
        lock (_lock)
        {
            _data[(spaceId, key)] = value;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _data.Count;
            }
        }
    }
}
