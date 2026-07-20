using System;

namespace ShimmerChatLib.Panel
{
	/// <summary>
	/// 标记插件面板组件的特性。
	/// NameKey / DescriptionKey 均为本地化 Key，由 LocService 解析为显示字符串。
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public class PluginPanelAttribute : Attribute
	{
		/// <summary>
		/// 面板名称本地化 Key（建议前缀 "panel.xxx"）
		/// </summary>
		public string NameKey { get; }

		/// <summary>
		/// 面板描述本地化 Key（建议前缀 "panel.xxx.desc"）
		/// </summary>
		public string DescriptionKey { get; }

		/// <summary>
		/// 面板图标（可选）
		/// </summary>
		public string? Icon { get; set; }

		/// <summary>
		/// 面板顺序（用于排序）
		/// </summary>
		public int Order { get; set; }

		public PanelDisplayPlace PanelDisplayPlace { get; set; }

		/// <summary>
		/// 构造函数
		/// </summary>
		/// <param name="nameKey">面板名称本地化 Key</param>
		/// <param name="descriptionKey">面板描述本地化 Key</param>
		public PluginPanelAttribute(string nameKey, string descriptionKey, PanelDisplayPlace panelDisplayPlace = PanelDisplayPlace.Settings)
		{
			NameKey = nameKey;
			DescriptionKey = descriptionKey;
			Order = 0; // 默认顺序
			PanelDisplayPlace = panelDisplayPlace;
		}
	}
	
	public enum PanelDisplayPlace
	{
		/// <summary>
		/// 无特殊参数
		/// </summary>
		Settings,
		/// <summary>
		/// 包含特殊参数：（[Parameter]）
		/// Guid AgentGuid { get; set; }
		/// Action<IChatPanelEventHandler> EventHandlerReg { get; set; }
		/// </summary>
		Agent,
		/// <summary>
		/// 实现需要包含特殊参数：（[Parameter]）
		/// Guid ChatGuid { get; set; }
		/// Guid AgentGuid { get; set; }
		/// Action<IChatPanelEventHandler> EventHandlerReg { get; set; }
		/// </summary>
		Chat 
	}
}