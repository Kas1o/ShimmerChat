using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 联网搜索工具集节点：把搜索工具、以及（提供方支持时）抓取工具加入本次生成的工具列表。
    /// 工具的参数（条数 / 是否取正文 / 截断长度）统一来自设置面板的全局配置。
    /// 提供方没有抓取接口时不添加抓取工具，并记录一条日志说明原因。
    /// </summary>
    [NodeInfo("node.web_search_tool_set", Icon = "🔎", Color = "var(--node-tool)",
        CategoryKeys = ["category.tool", "category.web_search"],
        DescriptionKey = "node.web_search_tool_set.desc")]
    public class WebSearchToolSetNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Web Search Tools";

        /// <summary>使用的提供方</summary>
        [NodeProperty("prop.web_search.provider", HintKey = "prop.web_search.provider.hint", Order = 1)]
        public WebSearchProviderKind Provider { get; set; } = WebSearchProviderKind.FromSettings;

        /// <summary>是否添加搜索工具</summary>
        [NodeProperty("prop.web_search_tool_set.enable_search", HintKey = "prop.web_search_tool_set.enable_search.hint", Order = 2)]
        public bool EnableSearchTool { get; set; } = true;

        /// <summary>是否添加抓取工具（提供方支持时）</summary>
        [NodeProperty("prop.web_search_tool_set.enable_fetch", HintKey = "prop.web_search_tool_set.enable_fetch.hint", Order = 3)]
        public bool EnableFetchTool { get; set; } = true;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;

            var (config, configError) = WebSearchConfigManager.Load(kvData);
            if (config == null)
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    loc["node_err.web_search_config_invalid"],
                    details: configError, nodeId: Id, nodeName: Name));

            var resolution = WebSearchProviderFactory.Resolve(config, Provider);
            if (resolution.Provider == null)
            {
                var name = WebSearchProviderCatalog.Find(resolution.ResolvedKind)?.DisplayName
                           ?? resolution.ResolvedKind.ToString();

                var message = resolution.Error == WebSearchResolutionError.ApiKeyMissing
                    ? loc.Format("node_err.web_search_api_key_missing", name)
                    : loc.Format("node_err.web_search_unknown_provider", name);

                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound, message, nodeId: Id, nodeName: Name));
            }

            var descriptor = WebSearchProviderCatalog.Get(resolution.ResolvedKind);

            if (EnableSearchTool)
                context.Env.Transient.Tools.Add(new WebSearchToolV2(kvData, Provider));

            if (EnableFetchTool)
            {
                if (descriptor.SupportsFetch)
                    context.Env.Transient.Tools.Add(new WebFetchToolV2(kvData, Provider));
                else
                    context.Env.Persistent.DebugOutput.Write("WebSearchToolSetNode", "WebSearch",
                        $"[{Name}] {descriptor.DisplayName} has no fetch API; web_fetch was not added.");
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }
    }
}
