using System.Text.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Memory
{
    public sealed record MemoryRecallEntry(string Id, float Score, string Content);

    public sealed class MemoryRecallSnapshot
    {
        public DateTimeOffset At { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public List<MemoryRecallEntry> Hits { get; set; } = [];
    }

    /// <summary>按对话记录最后一次记忆召回结果，供聊天侧栏展示。</summary>
    public static class MemoryRecallLog
    {
        private const string Space = "MemoryRecall";

        public static void Save(IKVDataService kv, Guid chatGuid, MemoryRecallSnapshot snapshot)
        {
            kv.Write(Space, chatGuid.ToString("N"), JsonSerializer.Serialize(snapshot));
        }

        public static MemoryRecallSnapshot? Load(IKVDataService kv, Guid chatGuid)
            => Load(kv, chatGuid, out _);

        public static MemoryRecallSnapshot? Load(IKVDataService kv, Guid chatGuid, out string? error)
        {
            error = null;
            var raw = kv.Read(Space, chatGuid.ToString("N"));
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                return JsonSerializer.Deserialize<MemoryRecallSnapshot>(raw);
            }
            catch (JsonException ex)
            {
                error = $"上次记忆召回记录损坏，无法解析：{ex.Message}";
                return null;
            }
        }
    }
}
