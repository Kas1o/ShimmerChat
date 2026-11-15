using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.HomeWidget
{
	/// <summary>
	/// 首页 Widget 类型元数据，供首页布局与添加面板使用。
	/// </summary>
	public sealed record HomeWidgetInfo(
		string Id,
		string NameKey,
		string? DescriptionKey,
		string Icon,
		int Order,
		HomeWidgetSize Size,
		bool AllowMultiple,
		bool IsDefault,
		Type ComponentType
	);

	/// <summary>
	/// 首页 Widget 注册表。扫描所有 <see cref="IHomeWidget"/> 实现及其
	/// <see cref="HomeWidgetAttribute"/> 元数据；扫描委托给 <see cref="IPluginLoaderService"/>，
	/// 因此插件提供的 Widget 与内置 Widget 同等被发现。
	/// </summary>
	public interface IHomeWidgetRegistry
	{
		/// <summary>所有已注册的 Widget，按 Order 排序。</summary>
		IReadOnlyList<HomeWidgetInfo> GetAll();

		/// <summary>
		/// 按 Id 查找 Widget。Id 为空或未注册时返回 null——
		/// 调用方需自行决定回退策略，注册表不做隐式修补。
		/// </summary>
		HomeWidgetInfo? GetById(string? id);

		/// <summary>首次运行时应默认出现在首页的 Widget，按 Order 排序。</summary>
		IReadOnlyList<HomeWidgetInfo> GetDefaults();
	}

	public class HomeWidgetRegistry : IHomeWidgetRegistry
	{
		private readonly Lazy<IReadOnlyList<HomeWidgetInfo>> _widgets;

		public HomeWidgetRegistry(IPluginLoaderService pluginLoader, ILogger<HomeWidgetRegistry> logger)
		{
			_widgets = new Lazy<IReadOnlyList<HomeWidgetInfo>>(() => ScanAll(pluginLoader, logger));
		}

		public IReadOnlyList<HomeWidgetInfo> GetAll() => _widgets.Value;

		public HomeWidgetInfo? GetById(string? id)
		{
			if (string.IsNullOrWhiteSpace(id)) return null;
			foreach (var widget in _widgets.Value)
			{
				if (string.Equals(widget.Id, id, StringComparison.Ordinal))
					return widget;
			}
			return null;
		}

		public IReadOnlyList<HomeWidgetInfo> GetDefaults()
			=> _widgets.Value.Where(w => w.IsDefault).ToList();

		private static List<HomeWidgetInfo> ScanAll(IPluginLoaderService pluginLoader, ILogger<HomeWidgetRegistry> logger)
		{
			var types = pluginLoader.GetTypesWithAttribute<HomeWidgetAttribute>();
			var result = new List<HomeWidgetInfo>(types.Count);
			var seenIds = new HashSet<string>(StringComparer.Ordinal);

			foreach (var type in types)
			{
				try
				{
					var attribute = type.GetCustomAttribute<HomeWidgetAttribute>(inherit: false);
					if (attribute == null)
					{
						logger.LogError(
							"首页 Widget 类型 {TypeName} 未标记 [HomeWidget]，已跳过。",
							type.FullName);
						continue;
					}

					if (!typeof(ComponentBase).IsAssignableFrom(type))
					{
						logger.LogError(
							"首页 Widget {TypeName} 不是 Blazor 组件（未继承 ComponentBase），已跳过。",
							type.FullName);
						continue;
					}

					if (!typeof(IHomeWidget).IsAssignableFrom(type))
					{
						logger.LogError(
							"首页 Widget {TypeName} 标记了 [HomeWidget] 但未实现 IHomeWidget，已跳过。",
							type.FullName);
						continue;
					}

					if (!seenIds.Add(attribute.Id))
					{
						logger.LogError(
							"首页 Widget Id '{WidgetId}' 重复（{TypeName}），该 Widget 已被跳过；" +
							"布局数据会引用先注册的那个。",
							attribute.Id, type.FullName);
						continue;
					}

					result.Add(new HomeWidgetInfo(
						attribute.Id,
						attribute.NameKey,
						attribute.DescriptionKey,
						attribute.Icon,
						attribute.Order,
						attribute.Size,
						attribute.AllowMultiple,
						attribute.IsDefault,
						type));
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "扫描首页 Widget 类型 {TypeName} 时出错: {Message}", type.FullName, ex.Message);
				}
			}

			return result
				.OrderBy(w => w.Order)
				.ThenBy(w => w.Id, StringComparer.Ordinal)
				.ToList();
		}
	}
}
