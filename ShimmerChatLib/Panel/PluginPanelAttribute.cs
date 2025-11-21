using System;

namespace ShimmerChatLib.Panel
{
	/// <summary>
	/// 标记插件面板组件的特性
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public class PluginPanelAttribute : Attribute
	{
		/// <summary>
		/// 面板名称
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// 面板描述
		/// </summary>
		public string Description { get; }

		/// <summary>
		/// 面板图标（可选）
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// 面板顺序（用于排序）
		/// </summary>
		public int Order { get; set; }

		/// <summary>
		/// 构造函数
		/// </summary>
		/// <param name="name">面板名称</param>
		/// <param name="description">面板描述</param>
		public PluginPanelAttribute(string name, string description)
		{
			Name = name;
			Description = description;
			Order = 0; // 默认顺序
		}
	}
}