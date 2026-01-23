using Microsoft.AspNetCore.Components;
using System.Collections.Generic;

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
        /// <param name="chat">当前的Chat对象</param>
        /// <param name="agent">当前的Agent对象</param>
        /// <returns>渲染后的HTML标记</returns>
        MarkupString Render(string markdownText, Chat? chat = null, Agent? agent = null);

        /// <summary>
        /// 获取所有加载的MessageRenderModifier
        /// </summary>
        List<IMessageRenderModifier> LoadedModifiers { get; }

        /// <summary>
        /// 获取所有激活的MessageRenderModifier及其输入值
        /// </summary>
        List<ActivatedMessageRenderModifier> ActivatedModifiers { get; }

        /// <summary>
        /// 激活一个MessageRenderModifier
        /// </summary>
        /// <param name="modifierName">MessageRenderModifier的名称</param>
        /// <param name="inputValue">输入值</param>
        void ActivateModifier(string modifierName, string inputValue);

        /// <summary>
        /// 移除指定索引的激活的MessageRenderModifier
        /// </summary>
        /// <param name="index">要移除的索引</param>
        void RemoveActivatedModifier(int index);

        /// <summary>
        /// 调整激活的MessageRenderModifier顺序
        /// </summary>
        /// <param name="oldIndex">原索引</param>
        /// <param name="newIndex">新索引</param>
        void ReorderActivatedModifier(int oldIndex, int newIndex);

        /// <summary>
        /// 保存激活的MessageRenderModifier配置
        /// </summary>
        void SaveActivatedModifiers();
    }

    /// <summary>
    /// 激活的MessageRenderModifier及其输入值
    /// </summary>
    public class ActivatedMessageRenderModifier
    {
        public required string Name { get; set; }
        public required string Value { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}