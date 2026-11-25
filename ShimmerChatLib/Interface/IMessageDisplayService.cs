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

        /// <summary>
        /// 带缓存写入提示的重载。流式生成中的消息应传 <paramref name="allowCacheWrites"/> = false：
        /// 内容每个增量都会变化，写入渲染缓存只会产生大量一次性 key。
        /// 默认实现忽略提示并转发给三参数版本，旧实现无需改动。
        /// </summary>
        MarkupString Render(string markdownText, Chat? chat, Agent? agent, bool allowCacheWrites)
            => Render(markdownText, chat, agent);

        /// <summary>调试模式开关</summary>
        bool DebugModeEnabled { get; set; }

        /// <summary>保存调试模式设置</summary>
        void SaveDebugModeSetting();
    }
}
