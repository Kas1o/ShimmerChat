using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.DynPrompt
{
    /// <summary>
    /// DynPrompt 注入槽的共享契约：元数据标记、识别与写入只在这里定义，
    /// 注入槽节点与 DynPrompt 节点读同一份定义，避免标记字符串出现平行副本。
    /// </summary>
    public static class DynPromptSlot
    {
        /// <summary>注入槽片段的元数据标记键。带此键的片段即注入位置。</summary>
        public const string MetadataKey = "dynprompt_slot";

        public static bool IsSlot(ContextSegment segment) =>
            segment.Metadata.ContainsKey(MetadataKey);

        /// <summary>把词条内容追加到注入槽；已有内容时以换行分隔，保持词条匹配顺序。</summary>
        public static void AppendContent(ContextSegment slot, string content)
        {
            slot.Message.Content = string.IsNullOrEmpty(slot.Message.Content)
                ? content
                : slot.Message.Content + "\n" + content;
        }
    }
}
