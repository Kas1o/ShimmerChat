using System;

namespace ShimmerChatLib.ChatView
{
	/// <summary>
	/// 对话界面元数据。标记在实现 <see cref="IChatView"/> 的 Blazor 组件上，
	/// 该组件即注册为一个可供 Agent 选择的对话界面（内置与插件同等地位）。
	/// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public class ChatViewAttribute : Attribute
	{
		/// <summary>
		/// 界面稳定标识。<see cref="Agent.ChatViewId"/> 引用此值，
		/// 因此重命名组件类型不应改变它。
		/// </summary>
		public string Id { get; }

		/// <summary>界面名称本地化 Key（建议前缀 "chatview."）。</summary>
		public string NameKey { get; }

		/// <summary>界面描述本地化 Key（可选）。</summary>
		public string? DescriptionKey { get; init; }

		/// <summary>界面图标（可选）。</summary>
		public string Icon { get; init; } = "💬";

		/// <summary>选择列表中的排序权重（越小越靠前）。</summary>
		public int Order { get; init; }

		/// <summary>
		/// 是否为默认界面。Agent 未指定界面（<see cref="Agent.ChatViewId"/> 为空）时使用。
		/// 多个界面同时声明默认时只取第一个（并记录错误日志）。
		/// </summary>
		public bool IsDefault { get; init; }

		public ChatViewAttribute(string id, string nameKey)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("对话界面 Id 不能为空。", nameof(id));
			if (string.IsNullOrWhiteSpace(nameKey))
				throw new ArgumentException("对话界面 NameKey 不能为空。", nameof(nameKey));

			Id = id;
			NameKey = nameKey;
		}
	}
}
