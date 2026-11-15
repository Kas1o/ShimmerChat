using System;

namespace ShimmerChatLib.HomeWidget
{
	/// <summary>
	/// 首页 Widget 的卡片尺寸。用于首页网格布局，不参与持久化。
	/// </summary>
	public enum HomeWidgetSize
	{
		/// <summary>占用一列。</summary>
		Normal = 0,
		/// <summary>在空间足够时占用两列；窄屏仍然回退为一列。</summary>
		Wide = 1
	}

	/// <summary>
	/// 标记一个首页 Widget 组件。标记在实现 <see cref="IHomeWidget"/> 的 Blazor 组件上，
	/// 该组件即注册为可添加到首页的 Widget（内置与插件同等地位）。
	/// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public sealed class HomeWidgetAttribute : Attribute
	{
		/// <summary>
		/// Widget 稳定标识。首页布局按此 Id 持久化引用，
		/// 因此重命名组件类型不应改变它。
		/// </summary>
		public string Id { get; }

		/// <summary>Widget 名称本地化 Key（建议前缀 "widget."）。</summary>
		public string NameKey { get; }

		/// <summary>Widget 描述本地化 Key（可选）。</summary>
		public string? DescriptionKey { get; init; }

		/// <summary>Widget 图标（Bootstrap Icons 类名，例如 "bi-robot"）。</summary>
		public string Icon { get; init; } = "bi-grid-1x2";

		/// <summary>添加面板中的排序权重（越小越靠前）。</summary>
		public int Order { get; init; }

		/// <summary>卡片在首页网格中的宽度。</summary>
		public HomeWidgetSize Size { get; init; } = HomeWidgetSize.Normal;

		/// <summary>
		/// 同一个首页 Layout 中是否允许出现多个实例。
		/// 例如“Agent 快捷入口”可以有多个，而“使用教学”通常只需要一个。
		/// </summary>
		public bool AllowMultiple { get; init; } = true;

		/// <summary>
		/// 首次运行（尚无已保存布局）时是否默认出现在首页。
		/// 用户保存过布局后，即使移除了默认 Widget 也不会被重新加回。
		/// </summary>
		public bool IsDefault { get; init; }

		public HomeWidgetAttribute(string id, string nameKey)
		{
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("首页 Widget Id 不能为空。", nameof(id));
			if (string.IsNullOrWhiteSpace(nameKey))
				throw new ArgumentException("首页 Widget NameKey 不能为空。", nameof(nameKey));

			Id = id;
			NameKey = nameKey;
		}
	}
}
