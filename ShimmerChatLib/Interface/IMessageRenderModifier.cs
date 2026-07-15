using System;

namespace ShimmerChatLib.Interface
{
    [Obsolete("Replaced by IRenderModifierNode pipeline")]
    public interface IMessageRenderModifier
    {
        public MessageRenderModifierInfo Info { get; }
        public string Modify(string content, string input, Chat? chat, Agent? agent);
    }

    public struct MessageRenderModifierInfo
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
    }
}
