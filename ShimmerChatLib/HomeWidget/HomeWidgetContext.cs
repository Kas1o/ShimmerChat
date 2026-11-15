using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShimmerChatLib.HomeWidget
{
	/// <summary>
	/// 首页布局中的一个 Widget 实例。WidgetId 引用 <see cref="HomeWidgetAttribute.Id"/>，
	/// Settings 为该实例的私有配置（例如 Agent 快捷入口绑定的 Agent）。
	/// </summary>
	public sealed class HomeWidgetInstance
	{
		/// <summary>
		/// 实例唯一标识，用于渲染时的 key 与布局持久化。
		/// 没有默认值：创建实例的一方必须显式赋值，布局反序列化时缺失该字段会作为非法数据被拒绝。
		/// </summary>
		public Guid InstanceId { get; set; }

		/// <summary>Widget 类型 Id（<see cref="HomeWidgetAttribute.Id"/>）。</summary>
		public string WidgetId { get; set; } = string.Empty;

		/// <summary>该实例的配置项。Key 由 Widget 自行定义。</summary>
		public Dictionary<string, string> Settings { get; set; } = new();
	}

	/// <summary>
	/// 交给首页 Widget 的宿主上下文。Widget 只通过这份契约保存自身配置，
	/// 不直接操作首页布局，也不持有页面实例。
	/// </summary>
	public sealed class HomeWidgetContext
	{
		/// <summary>当前 Widget 实例（配置读写都作用于此实例）。</summary>
		public required HomeWidgetInstance Instance { get; init; }

		/// <summary>
		/// 保存该实例的配置。返回 false 表示持久化失败，调用方必须向用户显式提示。
		/// </summary>
		public required Func<IReadOnlyDictionary<string, string>, Task<bool>> SaveSettingsAsync { get; init; }

		/// <summary>请求宿主重新渲染首页（例如设置变化后）。</summary>
		public required Func<Task> RequestRefreshAsync { get; init; }
	}
}
