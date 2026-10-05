using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatLib.Panel
{
	public interface IChatPanelEventHandler
	{
		void OnToolCallResult(Message message);
		void OnUserMessage(Message message);
		void OnAgentMessage(Message message);

		/// <summary>
		/// 由插件面板发起一次用户消息并启动生成。
		/// 面板在自己的界面内输入内容后调用此方法，无需依赖宿主页面的输入框。
		/// 默认实现不发送（返回 false），实现方可选择不支持该能力。
		/// </summary>
		/// <param name="message">要发送给当前对话的消息内容。</param>
		/// <returns>是否已受理并启动生成。</returns>
		Task<bool> SendUserMessageFromPanelAsync(string message) => Task.FromResult(false);

		/// <summary>
		/// 把文本写入当前对话的主输入框（不发送）。
		/// 仅在需要把内容交给用户继续编辑时使用；默认实现不处理。
		/// </summary>
		/// <param name="text">要写入输入框的文本。</param>
		/// <param name="append">true 追加到现有内容之后（换行分隔）；false 替换现有内容。</param>
		/// <returns>是否已写入。</returns>
		Task<bool> InsertIntoInputAsync(string text, bool append = true) => Task.FromResult(false);
	}
}
