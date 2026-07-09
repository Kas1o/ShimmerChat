namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 旧 ContextDocument 的兼容包装（过渡期使用，阶段5完成时删除）
    /// </summary>
    public class ContextDocument
    {
        public List<ContextSegment> Segments { get; set; } = new();
        public Dictionary<string, object> SharedState { get; set; } = new();

        public (SharperLLM.Util.ChatMessage, SharperLLM.Util.PromptBuilder.From)[] GetMessages()
        {
            return Segments
                .Select(s => (s.Message, s.From))
                .ToArray();
        }
    }
}
