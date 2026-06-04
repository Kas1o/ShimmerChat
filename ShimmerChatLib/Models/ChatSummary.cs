using System;

namespace ShimmerChatLib.Models
{
    public class ChatSummary
    {
        public Guid Guid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LastMessagePreview { get; set; } = string.Empty;
        public DateTime CreateTime { get; set; }
        public DateTime LastModifyTime { get; set; }
        public int MessageCount { get; set; }
    }
}
