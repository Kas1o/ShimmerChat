using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// 以 KVData 里 JSON 数组形式保存的条目列表 → 节点属性下拉选项。
    ///
    /// 读取失败（服务缺失 / JSON 损坏）返回空集合并写完整日志；数据还不存在时返回空集合但
    /// 不当作错误 —— 编辑器在这种情况下会显示「暂无可选项」提示，不会被误认为「一切正常」。
    /// </summary>
    public abstract class KVListOptionsProvider<TItem> : INodePropertyOptionsProvider
        where TItem : class
    {
        /// <inheritdoc/>
        public abstract string Key { get; }

        /// <summary>KVData 的 spaceId。</summary>
        protected abstract string SpaceId { get; }

        /// <summary>KVData 的 key。</summary>
        protected abstract string StoreKey { get; }

        /// <summary>写回节点属性的值。</summary>
        protected abstract string GetId(TItem item, IServiceProvider services);

        /// <summary>显示文本（提供者自行本地化）。</summary>
        protected abstract string GetLabel(TItem item, IServiceProvider services);

        /// <summary>分组名，null/空表示不分组。</summary>
        protected virtual string? GetGroup(TItem item, IServiceProvider services) => null;

        /// <summary>副标题，仅在属性打开 ShowOptionDescription 时显示。</summary>
        protected virtual string? GetDescription(TItem item, IServiceProvider services) => null;

        /// <summary>额外搜索词，只参与匹配、不显示。</summary>
        protected virtual IReadOnlyList<string>? GetSearchTerms(TItem item, IServiceProvider services) => null;

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            if (services.GetService(typeof(IKVDataService)) is not IKVDataService kv)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"选项提供者 {Key} 需要 IKVDataService，但服务容器里没有注册；该属性的选项将为空。");
                return [];
            }

            var json = kv.Read(SpaceId, StoreKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                // 尚未创建任何条目：由编辑器显示「暂无可选项」，这里不算错误。
                return [];
            }

            List<TItem>? items;
            try
            {
                items = JsonConvert.DeserializeObject<List<TItem>>(json);
            }
            catch (Exception ex)
            {
                OptionsProviderLog.Error(services, GetType(), ex,
                    $"读取 {SpaceId}/{StoreKey} 的选项数据失败，该属性的选项将为空。");
                return [];
            }

            if (items is null)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"{SpaceId}/{StoreKey} 的内容无法解析为 {typeof(TItem).Name} 列表，该属性的选项将为空。");
                return [];
            }

            return items.Select(item => new NodePropertyOption(
                    GetId(item, services),
                    GetLabel(item, services),
                    GetGroup(item, services),
                    GetDescription(item, services),
                    GetSearchTerms(item, services)))
                .ToList();
        }
    }

    /// <summary>选项提供者的日志出口：没有 ILoggerFactory 时静默降级（插件可能在没有日志宿主的场景被调用）。</summary>
    internal static class OptionsProviderLog
    {
        public static void Error(IServiceProvider services, Type owner, string message)
        {
            if (services.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory)
                factory.CreateLogger(owner.FullName ?? owner.Name).LogError("{Message}", message);
        }

        public static void Error(IServiceProvider services, Type owner, Exception ex, string message)
        {
            if (services.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory)
                factory.CreateLogger(owner.FullName ?? owner.Name).LogError(ex, "{Message}", message);
        }
    }

    /// <summary>
    /// 选项提供者使用的本地化帮助：取不到 LocService（或 Key 未定义）时用英文兜底，
    /// 保证选项列表永远能被读懂，而不是显示 Key 本身。
    /// </summary>
    internal static class OptionsProviderLoc
    {
        public static string Get(IServiceProvider services, string key, string fallback)
        {
            if (services.GetService(typeof(ILocService)) is not ILocService loc)
                return fallback;

            var value = loc[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }

        public static string Format(IServiceProvider services, string key, string fallback, params object[] args)
        {
            if (services.GetService(typeof(ILocService)) is not ILocService loc)
                return string.Format(fallback, args);

            var template = loc[key];
            if (string.IsNullOrWhiteSpace(template) || template == key)
                template = fallback;

            return string.Format(template, args);
        }
    }
}
