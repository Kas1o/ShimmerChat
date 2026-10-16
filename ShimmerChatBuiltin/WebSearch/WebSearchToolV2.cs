using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 联网搜索工具。搜索参数（条数 / 是否取正文 / 截断长度）统一来自设置面板中的全局配置，
    /// 节点只覆盖"用哪个提供方"。
    /// 供 ToolManager 直接勾选使用，也由 <see cref="WebSearchToolSetNode"/> 实例化。
    /// </summary>
    public sealed class WebSearchToolV2 : IAutoCreateToolV2
    {
        /// <summary>模型单次调用可请求的结果条数上限</summary>
        private const int MaxResultsCap = WebSearchConfigManager.MaxMaxResults;

        private readonly IKVDataService _kvData;
        private readonly WebSearchProviderKind _providerOverride;

        public static string NameKey => "tool.web_search";
        public static string DescriptionKey => "tool.web_search.desc";
        public static string[] CategoryKeys => ["category.web_search", "category.content"];

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new WebSearchToolV2(env.KVData, WebSearchProviderKind.FromSettings);

        public WebSearchToolV2(IKVDataService kvData, WebSearchProviderKind providerOverride)
        {
            _kvData = kvData;
            _providerOverride = providerOverride;
        }

        public Tool GetDefinition() => new()
        {
            name = "web_search",
            description =
                "Search the live web and return ranked results (title, URL, snippet, and page content when enabled). " +
                "Use it for current events, recent releases, or any fact that may have changed after the training cutoff.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "query",
                    type = ParameterType.String,
                    description = "The search query. Search-engine operators such as site:, quotes and -term are supported."
                }, true),
                (new ToolParameter
                {
                    name = "max_results",
                    type = ParameterType.Number,
                    description = $"Maximum number of results to return. Optional, defaults to the configured value (max {MaxResultsCap})."
                }, false)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            SearchInput arguments;
            try
            {
                arguments = JsonConvert.DeserializeObject<SearchInput>(input ?? "");
            }
            catch (JsonException ex)
            {
                return $"Error: invalid arguments JSON: {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(arguments.query))
                return "Error: 'query' parameter is required.";

            var (config, configError) = WebSearchConfigManager.Load(_kvData);
            if (config == null)
                return $"Error: web search configuration is invalid: {configError}";

            var resolution = WebSearchProviderFactory.Resolve(config, _providerOverride);
            if (resolution.Provider == null)
                return $"Error: {WebSearchProviderFactory.DescribeResolutionError(resolution)}";

            var maxResults = arguments.max_results is > 0
                ? Math.Min(arguments.max_results.Value, MaxResultsCap)
                : config.MaxResults;

            var query = new WebSearchQuery
            {
                Query = arguments.query.Trim(),
                MaxResults = maxResults,
                IncludeContent = config.IncludeContent,
                MaxContentChars = config.MaxContentChars,
                Language = string.IsNullOrWhiteSpace(config.Language) ? null : config.Language.Trim(),
                Country = string.IsNullOrWhiteSpace(config.Country) ? null : config.Country.Trim()
            };

            var response = await resolution.Provider.SearchAsync(query, CancellationToken.None);
            if (!response.Success)
                return $"Error: {response.Error}";

            if (response.Results.Count == 0)
                return $"No web search results found for \"{query.Query}\".";

            return WebSearchFormatter.FormatResults(
                response, query.Query, resolution.Provider.DisplayName, config.MaxContentChars);
        }

        private struct SearchInput
        {
            public string? query { get; set; }
            public int? max_results { get; set; }
        }
    }

    /// <summary>
    /// 网页抓取工具：把单个 URL 的正文取回来（走提供方的抓取接口，不由本进程直接访问目标站点）。
    /// 只有支持抓取的提供方才会被加入工具列表，见 <see cref="WebSearchProviderCatalog"/>。
    /// </summary>
    public sealed class WebFetchToolV2 : IAutoCreateToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly WebSearchProviderKind _providerOverride;

        public static string NameKey => "tool.web_fetch";
        public static string DescriptionKey => "tool.web_fetch.desc";
        public static string[] CategoryKeys => ["category.web_search", "category.content"];

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new WebFetchToolV2(env.KVData, WebSearchProviderKind.FromSettings);

        public WebFetchToolV2(IKVDataService kvData, WebSearchProviderKind providerOverride)
        {
            _kvData = kvData;
            _providerOverride = providerOverride;
        }

        public Tool GetDefinition() => new()
        {
            name = "web_fetch",
            description =
                "Fetch the text content of a single web page by URL, returned as markdown. " +
                "Use it after web_search to read a result in full.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "url",
                    type = ParameterType.String,
                    description = "The absolute http(s) URL of the page to fetch."
                }, true)
            ]
        };

        public async Task<string> ExecuteAsync(string input)
        {
            FetchInput arguments;
            try
            {
                arguments = JsonConvert.DeserializeObject<FetchInput>(input ?? "");
            }
            catch (JsonException ex)
            {
                return $"Error: invalid arguments JSON: {ex.Message}";
            }

            if (string.IsNullOrWhiteSpace(arguments.url))
                return "Error: 'url' parameter is required.";

            var url = arguments.url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return $"Error: 'url' must be an absolute http(s) URL, got '{url}'.";
            }

            var (config, configError) = WebSearchConfigManager.Load(_kvData);
            if (config == null)
                return $"Error: web search configuration is invalid: {configError}";

            var resolution = WebSearchProviderFactory.Resolve(config, _providerOverride);
            if (resolution.Provider == null)
                return $"Error: {WebSearchProviderFactory.DescribeResolutionError(resolution)}";

            var response = await resolution.Provider.FetchAsync(url, CancellationToken.None);
            if (!response.Success)
                return $"Error: {response.Error}";

            return WebSearchFormatter.FormatFetch(response, config.MaxFetchChars);
        }

        private struct FetchInput
        {
            public string? url { get; set; }
        }
    }
}
