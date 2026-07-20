using Microsoft.AspNetCore.Components;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 消息显示渲染服务。通过 IRenderModifierManager 执行渲染管线。
    /// </summary>
    public interface IMessageDisplayService
    {
        /// <summary>将 Markdown 文本渲染为 HTML</summary>
        MarkupString Render(string markdownText, Chat? chat = null, Agent? agent = null);

        /// <summary>调试模式开关</summary>
        bool DebugModeEnabled { get; set; }

        /// <summary>保存调试模式设置</summary>
        void SaveDebugModeSetting();
    }
}
