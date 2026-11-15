using System;
using System.Collections.Generic;

namespace ShimmerChatLib.HomeWidget
{
	/// <summary>
	/// 首页布局加载结果。失败时 <see cref="Widgets"/> 为空，调用方必须展示错误，
	/// 不得当作“空布局”静默处理。
	/// </summary>
	public sealed record HomeWidgetLayoutResult(
		bool Success,
		IReadOnlyList<HomeWidgetInstance> Widgets,
		string? ErrorCode = null,
		string? ErrorMessage = null,
		bool IsFirstRun = false);

	/// <summary>
	/// 首页布局保存结果。失败时布局数据没有被持久化，调用方必须回退并提示用户。
	/// </summary>
	public sealed record HomeWidgetLayoutSaveResult(
		bool Success,
		string? ErrorCode = null,
		string? ErrorMessage = null);

	/// <summary>首页布局的错误码。集中定义，避免 UI 与存储层各写一套字符串。</summary>
	public static class HomeWidgetLayoutErrorCodes
	{
		/// <summary>布局 JSON 无法解析。</summary>
		public const string InvalidJson = "home_widget.layout.invalid_json";

		/// <summary>布局数据版本高于当前程序支持的范围。</summary>
		public const string UnsupportedVersion = "home_widget.layout.unsupported_version";

		/// <summary>布局数据缺少必需字段，或包含无法识别的条目。</summary>
		public const string InvalidEntry = "home_widget.layout.invalid_entry";

		/// <summary>布局中存在重复的实例 Id。</summary>
		public const string DuplicateInstance = "home_widget.layout.duplicate_instance";

		/// <summary>读取或写入底层存储失败。</summary>
		public const string StorageFailure = "home_widget.layout.storage_failure";
	}

	/// <summary>
	/// 首页 Widget 布局持久化服务。布局以“Widget 实例列表”的形式保存，
	/// 数据不存在时返回注册表中声明为默认的 Widget，而不是空布局。
	/// </summary>
	public interface IHomeWidgetLayoutService
	{
		/// <summary>加载布局。首次运行时返回默认 Widget，并标记 <c>IsFirstRun = true</c>。</summary>
		HomeWidgetLayoutResult Load();

		/// <summary>保存布局。未知 WidgetId 允许保存（插件可能暂时未加载）。</summary>
		HomeWidgetLayoutSaveResult Save(IReadOnlyList<HomeWidgetInstance> widgets);

		/// <summary>按注册表默认值创建一份新的布局（不写入存储）。</summary>
		IReadOnlyList<HomeWidgetInstance> CreateDefaultLayout();
	}
}
