using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.HomeWidget;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
	/// <summary>
	/// 首页 Widget 布局的持久化实现。布局存放在 KV 空间 <c>Home</c> 的
	/// <c>WidgetLayout</c> 键下，带版本号以便未来迁移。
	/// </summary>
	public sealed class HomeWidgetLayoutService : IHomeWidgetLayoutService
	{
		private const string KvSpace = "Home";
		private const string KvKey = "WidgetLayout";
		private const int CurrentVersion = 1;

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNameCaseInsensitive = true
		};

		private readonly IKVDataService _kvData;
		private readonly IHomeWidgetRegistry _registry;
		private readonly ILogger<HomeWidgetLayoutService> _logger;

		public HomeWidgetLayoutService(
			IKVDataService kvData,
			IHomeWidgetRegistry registry,
			ILogger<HomeWidgetLayoutService> logger)
		{
			_kvData = kvData;
			_registry = registry;
			_logger = logger;
		}

		public HomeWidgetLayoutResult Load()
		{
			string? raw;
			try
			{
				raw = _kvData.Read(KvSpace, KvKey);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[HomeWidget] Failed to read the widget layout from storage.");
				return Failure(HomeWidgetLayoutErrorCodes.StorageFailure, ex.Message);
			}

			if (raw == null)
			{
				return new HomeWidgetLayoutResult(true, CreateDefaultLayout(), IsFirstRun: true);
			}

			HomeWidgetLayoutData? data;
			try
			{
				data = JsonSerializer.Deserialize<HomeWidgetLayoutData>(raw, JsonOptions);
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "[HomeWidget] The persisted widget layout is not valid JSON.");
				return Failure(HomeWidgetLayoutErrorCodes.InvalidJson, ex.Message);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[HomeWidget] Failed to deserialize the persisted widget layout.");
				return Failure(HomeWidgetLayoutErrorCodes.InvalidJson, ex.Message);
			}

			if (data == null || data.Widgets == null)
			{
				_logger.LogError("[HomeWidget] The persisted widget layout is empty or missing the widgets array.");
				return Failure(HomeWidgetLayoutErrorCodes.InvalidEntry, "Layout data is empty.");
			}

			if (data.Version != CurrentVersion)
			{
				_logger.LogError(
					"[HomeWidget] Unsupported widget layout version {Version}; this build supports version {Supported}.",
					data.Version, CurrentVersion);
				return Failure(
					HomeWidgetLayoutErrorCodes.UnsupportedVersion,
					$"Layout version {data.Version} is not supported (expected {CurrentVersion}).");
			}

			var seenInstanceIds = new HashSet<Guid>();
			foreach (var widget in data.Widgets)
			{
				if (widget == null)
				{
					_logger.LogError("[HomeWidget] The persisted widget layout contains a null entry.");
					return Failure(HomeWidgetLayoutErrorCodes.InvalidEntry, "Layout contains a null widget entry.");
				}

				if (string.IsNullOrWhiteSpace(widget.WidgetId))
				{
					_logger.LogError(
						"[HomeWidget] Widget instance {InstanceId} has an empty WidgetId.",
						widget.InstanceId);
					return Failure(HomeWidgetLayoutErrorCodes.InvalidEntry, "A widget entry has an empty WidgetId.");
				}

				if (widget.InstanceId == Guid.Empty)
				{
					_logger.LogError(
						"[HomeWidget] Widget {WidgetId} has an empty instance id.",
						widget.WidgetId);
					return Failure(HomeWidgetLayoutErrorCodes.InvalidEntry, "A widget entry has an empty InstanceId.");
				}

				if (!seenInstanceIds.Add(widget.InstanceId))
				{
					_logger.LogError(
						"[HomeWidget] Duplicate widget instance id {InstanceId} in the persisted layout.",
						widget.InstanceId);
					return Failure(
						HomeWidgetLayoutErrorCodes.DuplicateInstance,
						$"Duplicate widget instance id: {widget.InstanceId}");
				}

				if (widget.Settings == null)
				{
					// 缺失 Settings 是可恢复的边界数据：补空集合并明确告警。
					_logger.LogWarning(
						"[HomeWidget] Widget instance {InstanceId} ({WidgetId}) has no settings object; using an empty one.",
						widget.InstanceId, widget.WidgetId);
					widget.Settings = new Dictionary<string, string>();
				}
			}

			return new HomeWidgetLayoutResult(true, data.Widgets);
		}

		public HomeWidgetLayoutSaveResult Save(IReadOnlyList<HomeWidgetInstance> widgets)
		{
			if (widgets == null)
			{
				_logger.LogError("[HomeWidget] Rejected a null widget layout.");
				return new HomeWidgetLayoutSaveResult(
					false, HomeWidgetLayoutErrorCodes.InvalidEntry, "The widget layout is null.");
			}

			// 写入前先阻止会让自己下次无法加载的数据（空 Id / 重复实例 Id）。
			var seenInstanceIds = new HashSet<Guid>();
			foreach (var widget in widgets)
			{
				if (widget == null || string.IsNullOrWhiteSpace(widget.WidgetId) || widget.InstanceId == Guid.Empty)
				{
					_logger.LogError("[HomeWidget] Rejected an invalid widget entry while saving the layout.");
					return new HomeWidgetLayoutSaveResult(
						false, HomeWidgetLayoutErrorCodes.InvalidEntry,
						"The layout contains an entry without a WidgetId or InstanceId.");
				}

				if (!seenInstanceIds.Add(widget.InstanceId))
				{
					_logger.LogError(
						"[HomeWidget] Rejected duplicate widget instance id {InstanceId} while saving the layout.",
						widget.InstanceId);
					return new HomeWidgetLayoutSaveResult(
						false, HomeWidgetLayoutErrorCodes.DuplicateInstance,
						$"Duplicate widget instance id: {widget.InstanceId}");
				}
			}

			try
			{
				var data = new HomeWidgetLayoutData
				{
					Version = CurrentVersion,
					Widgets = widgets.ToList()
				};
				_kvData.Write(KvSpace, KvKey, JsonSerializer.Serialize(data));
				return new HomeWidgetLayoutSaveResult(true);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[HomeWidget] Failed to persist the widget layout.");
				return new HomeWidgetLayoutSaveResult(
					false, HomeWidgetLayoutErrorCodes.StorageFailure, ex.Message);
			}
		}

		public IReadOnlyList<HomeWidgetInstance> CreateDefaultLayout()
		{
			return _registry.GetDefaults()
				.Select(widget => new HomeWidgetInstance
				{
					InstanceId = Guid.NewGuid(),
					WidgetId = widget.Id
				})
				.ToList();
		}

		private static HomeWidgetLayoutResult Failure(string code, string message)
			=> new(false, Array.Empty<HomeWidgetInstance>(), code, message);

		/// <summary>持久化结构。Version 用于未来布局格式迁移。</summary>
		private sealed class HomeWidgetLayoutData
		{
			public int Version { get; set; }
			public List<HomeWidgetInstance>? Widgets { get; set; }
		}
	}
}
