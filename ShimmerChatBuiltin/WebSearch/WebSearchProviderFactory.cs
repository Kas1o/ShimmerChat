using System.Net.Http;
using ShimmerChatBuiltin.WebSearch.Providers;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 依据配置解析出可用的提供方实例。
    /// 解析失败不抛异常也不静默回落，而是返回带原因的 <see cref="WebSearchProviderResolution"/>，
    /// 由节点 / 工具决定如何汇报（节点本地化，工具给英文文本）。
    /// </summary>
    public static class WebSearchProviderFactory
    {
        /// <summary>
        /// 解析提供方。<paramref name="requested"/> 为 <see cref="WebSearchProviderKind.FromSettings"/> 时使用配置中的当前提供方。
        /// </summary>
        public static WebSearchProviderResolution Resolve(
            WebSearchConfig config, WebSearchProviderKind requested, HttpClient? http = null)
        {
            var kind = requested == WebSearchProviderKind.FromSettings ? config.ActiveProvider : requested;

            if (WebSearchProviderCatalog.Find(kind) == null)
                return new WebSearchProviderResolution { Error = WebSearchResolutionError.UnknownProvider, ResolvedKind = kind };

            var settings = config.GetProviderSettings(kind);
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                return new WebSearchProviderResolution { Error = WebSearchResolutionError.ApiKeyMissing, ResolvedKind = kind };

            var provider = Create(kind, settings, TimeSpan.FromSeconds(config.TimeoutSeconds), http);
            return new WebSearchProviderResolution { Provider = provider, ResolvedKind = kind };
        }

        /// <summary>按提供方创建实例。<paramref name="http"/> 为空时使用共享 HttpClient。</summary>
        public static IWebSearchProvider Create(
            WebSearchProviderKind kind, WebSearchProviderSettings settings, TimeSpan timeout, HttpClient? http = null)
        {
            var client = http ?? WebSearchHttp.Client;
            var baseUrl = settings.BaseUrl;

            return kind switch
            {
                WebSearchProviderKind.DeepSeek => new DeepSeekSearchProvider(client, settings.ApiKey, baseUrl, timeout),
                WebSearchProviderKind.Tavily => new TavilySearchProvider(client, settings.ApiKey, baseUrl, timeout),
                WebSearchProviderKind.Brave => new BraveSearchProvider(client, settings.ApiKey, baseUrl, timeout),
                WebSearchProviderKind.Exa => new ExaSearchProvider(client, settings.ApiKey, baseUrl, timeout),
                WebSearchProviderKind.SerpApi => new SerpApiSearchProvider(client, settings.ApiKey, baseUrl, timeout),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "Cannot create a web search provider for this kind. Use WebSearchProviderCatalog to enumerate supported kinds.")
            };
        }

        /// <summary>解析失败的英文说明（工具输出用；节点使用本地化文本）</summary>
        public static string DescribeResolutionError(WebSearchProviderResolution resolution) => resolution.Error switch
        {
            WebSearchResolutionError.UnknownProvider =>
                $"Unknown web search provider '{resolution.ResolvedKind}'.",
            WebSearchResolutionError.ApiKeyMissing =>
                $"No API key configured for {DisplayName(resolution.ResolvedKind)}. " +
                "Open Settings → Web Search to configure it.",
            _ => "Web search provider resolution failed."
        };

        private static string DisplayName(WebSearchProviderKind kind) =>
            WebSearchProviderCatalog.Find(kind)?.DisplayName ?? kind.ToString();
    }
}
