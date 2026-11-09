using Microsoft.Extensions.DependencyInjection;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// 可实例化工具的类型下拉：标签/描述/分组全部来自工具注册表（IToolRegistry），
    /// 不在这里维护第二份工具清单。
    /// </summary>
    public sealed class ToolTypeOptionsProvider : INodePropertyOptionsProvider
    {
        public string Key => "tool_types";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            if (services.GetService(typeof(IToolRegistry)) is not IToolRegistry registry)
            {
                OptionsProviderLog.Error(services, GetType(),
                    "工具类型选项需要 IToolRegistry，但服务容器里没有注册；该属性的选项将为空。");
                return [];
            }

            return registry.AllTools
                .OrderBy(t => GroupOf(t, services) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TypeName, StringComparer.Ordinal)
                .Select(tool => new NodePropertyOption(
                    tool.TypeName,
                    OptionsProviderLoc.Get(services, tool.NameKey, tool.TypeName),
                    GroupOf(tool, services),
                    OptionsProviderLoc.Get(services, tool.DescriptionKey, string.Empty),
                    [tool.TypeName, tool.NameKey]))
                .ToList();
        }

        /// <summary>
        /// CategoryKeys 是分层的本地化 Key（可能写成 "category.flow/branch"），
        /// 这里按 "/" 展开后逐段本地化，再用 " / " 连接成可读的分组名。
        /// </summary>
        private static string? GroupOf(ToolMetadata tool, IServiceProvider services)
        {
            if (tool.CategoryKeys is not { Length: > 0 })
                return null;

            var parts = tool.CategoryKeys
                .SelectMany(k => k.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(segment => OptionsProviderLoc.Get(services, segment, segment))
                .ToList();

            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }
    }
}
