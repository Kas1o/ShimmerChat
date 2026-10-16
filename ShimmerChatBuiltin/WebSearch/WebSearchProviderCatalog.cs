namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 提供方静态描述。能力标记（<see cref="SupportsContent"/> / <see cref="SupportsFetch"/>）以此处为唯一来源：
    /// 界面用它决定显示哪些能力，工具集节点用它决定是否添加抓取工具。
    /// </summary>
    /// <param name="Kind">提供方标识</param>
    /// <param name="DisplayName">给模型看的英文名（技术名，不本地化）</param>
    /// <param name="NameKey">本地化名称 Key</param>
    /// <param name="DescriptionKey">本地化描述 Key</param>
    /// <param name="DocsUrl">官方文档 / 申请 API Key 的地址</param>
    /// <param name="DefaultBaseUrl">默认 API 根地址</param>
    /// <param name="SupportsContent">搜索接口是否可直接返回网页正文</param>
    /// <param name="SupportsFetch">是否提供单页抓取接口</param>
    public sealed record WebSearchProviderDescriptor(
        WebSearchProviderKind Kind,
        string DisplayName,
        string NameKey,
        string DescriptionKey,
        string DocsUrl,
        string DefaultBaseUrl,
        bool SupportsContent,
        bool SupportsFetch);

    /// <summary>内置的五个联网搜索服务方</summary>
    public static class WebSearchProviderCatalog
    {
        public static IReadOnlyList<WebSearchProviderDescriptor> All { get; } =
        [
            new(WebSearchProviderKind.DeepSeek,
                "DeepSeek",
                "websearch.provider.deepseek",
                "websearch.provider.deepseek.desc",
                "https://api-docs.deepseek.com/zh-cn/guides/search",
                "https://api.deepseek.com",
                SupportsContent: true,
                SupportsFetch: true),

            new(WebSearchProviderKind.Tavily,
                "Tavily",
                "websearch.provider.tavily",
                "websearch.provider.tavily.desc",
                "https://docs.tavily.com/",
                "https://api.tavily.com",
                SupportsContent: true,
                SupportsFetch: true),

            new(WebSearchProviderKind.Brave,
                "Brave Search",
                "websearch.provider.brave",
                "websearch.provider.brave.desc",
                "https://api-dashboard.search.brave.com/app/documentation/web-search/get-started",
                "https://api.search.brave.com",
                SupportsContent: false,
                SupportsFetch: false),

            new(WebSearchProviderKind.Exa,
                "Exa",
                "websearch.provider.exa",
                "websearch.provider.exa.desc",
                "https://docs.exa.ai/",
                "https://api.exa.ai",
                SupportsContent: true,
                SupportsFetch: true),

            new(WebSearchProviderKind.SerpApi,
                "SerpApi",
                "websearch.provider.serpapi",
                "websearch.provider.serpapi.desc",
                "https://serpapi.com/search-api",
                "https://serpapi.com",
                SupportsContent: false,
                SupportsFetch: false)
        ];

        public static WebSearchProviderDescriptor? Find(WebSearchProviderKind kind) =>
            All.FirstOrDefault(d => d.Kind == kind);

        /// <summary>取描述，找不到时抛出——调用前必须保证 kind 来自本目录</summary>
        public static WebSearchProviderDescriptor Get(WebSearchProviderKind kind) =>
            Find(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind,
                "Unknown web search provider kind. Only kinds listed in WebSearchProviderCatalog are supported.");
    }
}
