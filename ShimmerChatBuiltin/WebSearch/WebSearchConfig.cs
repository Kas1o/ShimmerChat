using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>单个提供方的连接配置</summary>
    public sealed class WebSearchProviderSettings
    {
        /// <summary>API Key（各服务方申请，见 catalog 中的 DocsUrl）</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>自定义 API 根地址，留空使用官方地址</summary>
        public string BaseUrl { get; set; } = "";
    }

    /// <summary>全局联网搜索配置（KVData "WebSearch/Config"）</summary>
    public sealed class WebSearchConfig
    {
        /// <summary>默认使用的提供方（节点未覆盖时生效）</summary>
        public WebSearchProviderKind ActiveProvider { get; set; } = WebSearchProviderKind.Tavily;

        public Dictionary<WebSearchProviderKind, WebSearchProviderSettings> Providers { get; set; } = [];

        /// <summary>默认结果条数</summary>
        public int MaxResults { get; set; } = 5;

        /// <summary>请求超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>是否请求服务方返回网页正文（支持的服务方才有意义）</summary>
        public bool IncludeContent { get; set; }

        /// <summary>注入上下文 / 返回给模型的单条正文最大字符数</summary>
        public int MaxContentChars { get; set; } = 2000;

        /// <summary>抓取工具返回的单页正文最大字符数</summary>
        public int MaxFetchChars { get; set; } = 20000;

        /// <summary>结果语言偏好，留空不传（仅部分提供方支持）</summary>
        public string Language { get; set; } = "";

        /// <summary>结果地区偏好，留空不传（仅部分提供方支持）</summary>
        public string Country { get; set; } = "";

        /// <summary>读取某提供方的连接配置，缺失时返回空配置（后续会以「未配置 API Key」明确报错）</summary>
        public WebSearchProviderSettings GetProviderSettings(WebSearchProviderKind kind)
        {
            if (Providers != null && Providers.TryGetValue(kind, out var settings) && settings != null)
                return settings;
            return new WebSearchProviderSettings();
        }
    }

    /// <summary>WebSearchConfig 的 KVData 读写</summary>
    public static class WebSearchConfigManager
    {
        private const string SpaceId = "WebSearch";
        private const string Key = "Config";

        /// <summary>超时下限 / 上限（秒）</summary>
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 600;

        /// <summary>结果条数下限 / 上限</summary>
        public const int MinMaxResults = 1;
        public const int MaxMaxResults = 50;

        /// <summary>
        /// 读取配置。未写入过时返回默认配置；JSON 损坏或取值非法时返回错误信息，
        /// 由调用方决定如何汇报（不静默回落成默认值）。
        /// </summary>
        public static (WebSearchConfig? Config, string? Error) Load(IKVDataService kvData)
        {
            var json = kvData.Read(SpaceId, Key);
            if (string.IsNullOrWhiteSpace(json))
                return (new WebSearchConfig(), null);

            WebSearchConfig? config;
            try
            {
                config = JsonConvert.DeserializeObject<WebSearchConfig>(json);
            }
            catch (JsonException ex)
            {
                return (null, $"Web search config is not valid JSON: {ex.Message}");
            }

            if (config == null)
                return (null, "Web search config deserialized to null.");

            var error = Validate(config);
            return error == null ? (config, null) : (null, error);
        }

        /// <summary>
        /// 校验配置取值。返回错误说明，null 表示合法。
        /// 面板保存与 KVData 读取共用同一判定，避免两边规则不一致。
        /// </summary>
        public static string? Validate(WebSearchConfig config)
        {
            if (WebSearchProviderCatalog.Find(config.ActiveProvider) == null)
                return $"ActiveProvider is not a concrete provider: {config.ActiveProvider}.";

            if (config.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                return $"TimeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}, got {config.TimeoutSeconds}.";

            if (config.MaxResults is < MinMaxResults or > MaxMaxResults)
                return $"MaxResults must be between {MinMaxResults} and {MaxMaxResults}, got {config.MaxResults}.";

            if (config.MaxContentChars < 0)
                return $"MaxContentChars must not be negative, got {config.MaxContentChars}.";

            if (config.MaxFetchChars < 0)
                return $"MaxFetchChars must not be negative, got {config.MaxFetchChars}.";

            if (config.Providers == null)
                return null;

            foreach (var (kind, settings) in config.Providers)
            {
                if (settings == null) continue;

                var baseUrl = settings.BaseUrl?.Trim();
                if (string.IsNullOrEmpty(baseUrl)) continue;

                // 非法地址会在构造请求时抛 UriFormatException，提前拦下并明确指出是哪个服务
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return $"{kind} base URL must be an absolute http(s) URL, got '{settings.BaseUrl}'.";
                }
            }

            return null;
        }

        public static void Save(IKVDataService kvData, WebSearchConfig config)
        {
            kvData.Write(SpaceId, Key, JsonConvert.SerializeObject(config, Formatting.Indented));
        }
    }
}
