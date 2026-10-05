using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.ChatView
{
	/// <summary>
	/// 对话界面类型元数据，供 Agent 设置界面与聊天页解析使用。
	/// </summary>
	public sealed record ChatViewInfo(
		string Id,
		string NameKey,
		string? DescriptionKey,
		string Icon,
		int Order,
		bool IsDefault,
		Type ComponentType
	);

	/// <summary>
	/// 对话界面注册表。扫描所有 <see cref="IChatView"/> 实现及其
	/// <see cref="ChatViewAttribute"/> 元数据；扫描委托给 <see cref="IPluginLoaderService"/>，
	/// 因此插件提供的界面与内置界面同等被发现。
	/// </summary>
	public interface IChatViewRegistry
	{
		/// <summary>所有已注册的对话界面，按 Order 排序。</summary>
		IReadOnlyList<ChatViewInfo> GetAll();

		/// <summary>
		/// 按 Id 查找界面。Id 为空或未注册时返回 null——
		/// 调用方需自行决定回退策略，注册表不做隐式修补。
		/// </summary>
		ChatViewInfo? GetById(string? id);

		/// <summary>
		/// 默认界面（声明了 <see cref="ChatViewAttribute.IsDefault"/> 的界面；
		/// 无人声明时取排序后的第一个）。没有任何界面时返回 null。
		/// </summary>
		ChatViewInfo? GetDefault();
	}

	public class ChatViewRegistry : IChatViewRegistry
	{
		private readonly Lazy<IReadOnlyList<ChatViewInfo>> _views;

		public ChatViewRegistry(IPluginLoaderService pluginLoader, ILogger<ChatViewRegistry> logger)
		{
			_views = new Lazy<IReadOnlyList<ChatViewInfo>>(() => ScanAll(pluginLoader, logger));
		}

		public IReadOnlyList<ChatViewInfo> GetAll() => _views.Value;

		public ChatViewInfo? GetById(string? id)
		{
			if (string.IsNullOrWhiteSpace(id)) return null;
			var views = _views.Value;
			foreach (var view in views)
			{
				if (string.Equals(view.Id, id, StringComparison.Ordinal))
					return view;
			}
			return null;
		}

		public ChatViewInfo? GetDefault()
		{
			var views = _views.Value;
			return views.FirstOrDefault(v => v.IsDefault) ?? views.FirstOrDefault();
		}

		private static List<ChatViewInfo> ScanAll(IPluginLoaderService pluginLoader, ILogger<ChatViewRegistry> logger)
		{
			var types = pluginLoader.GetImplementingTypes(typeof(IChatView));
			var result = new List<ChatViewInfo>(types.Count);
			var seenIds = new HashSet<string>(StringComparer.Ordinal);

			foreach (var type in types)
			{
				try
				{
					var attribute = type.GetCustomAttribute<ChatViewAttribute>(inherit: false);
					if (attribute == null)
					{
						logger.LogError(
							"对话界面 {TypeName} 实现了 IChatView 但未标记 [ChatView]，已跳过。",
							type.FullName);
						continue;
					}

					if (!typeof(ComponentBase).IsAssignableFrom(type))
					{
						logger.LogError(
							"对话界面 {TypeName} 不是 Blazor 组件（未继承 ComponentBase），已跳过。",
							type.FullName);
						continue;
					}

					if (!seenIds.Add(attribute.Id))
					{
						logger.LogError(
							"对话界面 Id '{ViewId}' 重复（{TypeName}），该界面已被跳过；" +
							"Agent 选择界面时会引用先注册的那个。",
							attribute.Id, type.FullName);
						continue;
					}

					result.Add(new ChatViewInfo(
						attribute.Id,
						attribute.NameKey,
						attribute.DescriptionKey,
						attribute.Icon,
						attribute.Order,
						attribute.IsDefault,
						type));
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "扫描对话界面类型 {TypeName} 时出错: {Message}", type.FullName, ex.Message);
				}
			}

			var defaults = result.Where(v => v.IsDefault).ToList();
			if (defaults.Count > 1)
			{
				logger.LogError(
					"有 {Count} 个对话界面声明为默认（{Ids}），仅使用排序后的第一个。",
					defaults.Count, string.Join(", ", defaults.Select(v => v.Id)));
			}

			return result
				.OrderBy(v => v.Order)
				.ThenBy(v => v.Id, StringComparer.Ordinal)
				.ToList();
		}
	}
}
