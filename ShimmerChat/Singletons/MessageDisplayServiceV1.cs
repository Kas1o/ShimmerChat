using Markdig;
using Markdig.Extensions.Tables;
using Microsoft.AspNetCore.Components;

namespace ShimmerChat.Singletons
{
    /// <summary>
    /// 消息显示渲染服务的具体实现
    /// 使用单例模式管理Markdown渲染管道，避免重复创建消耗资源
    /// </summary>
    public class MessageDisplayServiceV1 : IMessageDisplayService
    {
        // 共享的MarkdownPipeline实例，只需创建一次
        private readonly MarkdownPipeline _markdownPipeline;

        /// <summary>
        /// 构造函数
        /// 初始化Markdown渲染管道，配置所需的扩展
        /// </summary>
        public MessageDisplayServiceV1()
        {
            // 创建并配置MarkdownPipeline
            // 只在服务初始化时创建一次，所有消息组件共享使用
            _markdownPipeline = new MarkdownPipelineBuilder()
                .UsePipeTables() // 启用表格支持
                // 未来可以在这里添加更多扩展，如LaTeX支持等
                .Build();
        }

        /// <summary>
        /// 将Markdown文本渲染为HTML标记
        /// 使用共享的渲染管道，提高性能
        /// </summary>
        /// <param name="markdownText">要渲染的Markdown文本</param>
        /// <returns>渲染后的HTML标记</returns>
        public MarkupString RenderMarkdown(string markdownText)
        {
            // 使用共享的MarkdownPipeline实例进行渲染
            // 为空文本提供默认值，避免空引用异常
            return (MarkupString)Markdig.Markdown.ToHtml(markdownText ?? "", _markdownPipeline);
        }
    }
}