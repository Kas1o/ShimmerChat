using ShimmerChatBuiltin.Mcp;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Options
{
    /// <summary>
    /// MCP 端点目录 → 节点属性下拉选项。
    /// 与设置页共用 <see cref="McpEndpointCatalog"/>，`Id` 是写回节点的唯一引用键。
    /// 目录损坏 / 服务缺失时返回空集合并写日志，由编辑器显示「暂无可选项」。
    /// </summary>
    public sealed class McpEndpointOptionsProvider : INodePropertyOptionsProvider
    {
        /// <inheritdoc/>
        public string Key => "mcp_endpoints";

        public IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services)
        {
            if (services.GetService(typeof(IKVDataService)) is not IKVDataService kv)
            {
                OptionsProviderLog.Error(services, GetType(),
                    $"选项提供者 {Key} 需要 IKVDataService，但服务容器里没有注册；该属性的选项将为空。");
                return [];
            }

            List<McpEndpointConfig> endpoints;
            try
            {
                endpoints = McpEndpointCatalog.Load(kv);
            }
            catch (Exception ex)
            {
                // 目录格式错误属于边界失败：UI 不能崩，但必须留下完整错误。
                OptionsProviderLog.Error(services, GetType(), ex,
                    "读取 MCP 端点目录失败，节点属性「端点」的选项将为空。");
                return [];
            }

            if (endpoints.Count == 0)
                return [];

            var disabled = OptionsProviderLoc.Get(services, "options.disabled", "Disabled");

            return endpoints.Select(endpoint =>
            {
                var label = string.IsNullOrWhiteSpace(endpoint.Name) ? endpoint.Id : endpoint.Name;
                var target = endpoint.Transport == McpTransportType.Stdio
                    ? string.Join(' ', new[] { endpoint.Command }.Concat(endpoint.Arguments))
                        .Trim()
                    : endpoint.Url;

                var description = endpoint.Enabled
                    ? target
                    : string.IsNullOrWhiteSpace(target) ? disabled : $"{disabled} · {target}";

                return new NodePropertyOption(
                    endpoint.Id,
                    label,
                    endpoint.Transport.ToString(),
                    description,
                    [
                        endpoint.Id,
                        endpoint.Name,
                        endpoint.Command,
                        endpoint.Url,
                        .. endpoint.Arguments,
                        .. endpoint.Tools.Where(t => t != null).Select(t => t.Name)
                    ]);
            }).ToList();
        }
    }
}
