namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 内容在界面上的渲染位置。渲染节点据此区分同一段文本的不同显示区域，
    /// 例如只对消息正文做 Markdown 渲染，而思维链保持原样。
    /// </summary>
    public enum RenderPosition
    {
        /// <summary>消息正文（聊天气泡正文、工具结果、子智能体会话正文）。</summary>
        Body,

        /// <summary>思维链（thinking / reasoning）。</summary>
        Thinking
    }
}
