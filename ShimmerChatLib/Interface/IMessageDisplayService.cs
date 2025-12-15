using Microsoft.AspNetCore.Components;
namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 消息显示渲染服务接口
    /// 提供统一的消息内容渲染功能，包括Markdown解析和未来可能的其他渲染功能
    /// </summary>
    public interface IMessageDisplayService
    {
        /// <summary>
        /// 将Markdown文本渲染为HTML
        /// </summary>
        /// <param name="markdownText">要渲染的Markdown文本</param>
        /// <returns>渲染后的HTML标记</returns>
        MarkupString RenderMarkdown(string markdownText);
    }
}