namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 联网搜索服务提供方。
    /// <see cref="FromSettings"/> 只允许出现在节点属性里（表示「跟随设置面板中选择的提供方」），
    /// 配置中的 ActiveProvider 必须是具体的服务方。
    /// </summary>
    public enum WebSearchProviderKind
    {
        FromSettings = 0,
        DeepSeek = 1,
        Tavily = 2,
        Brave = 3,
        Exa = 4,
        SerpApi = 5
    }

    /// <summary>自动搜索节点的查询词来源</summary>
    public enum WebSearchQuerySource
    {
        /// <summary>最近一条用户消息（本次输入内容）</summary>
        LastUserMessage = 0,

        /// <summary>节点中固定的查询词</summary>
        StaticText = 1
    }

    /// <summary>节点取不到查询词 / 搜索失败时的行为</summary>
    public enum WebSearchFailureBehavior
    {
        /// <summary>向上汇报失败，终止本次生成（默认）</summary>
        FailGeneration = 0,

        /// <summary>写入 DebugOutput 后跳过本次注入，继续生成</summary>
        SkipAndLog = 1
    }

    /// <summary>一次搜索请求的参数</summary>
    public sealed class WebSearchQuery
    {
        public required string Query { get; init; }

        /// <summary>期望返回的结果条数</summary>
        public int MaxResults { get; init; } = 5;

        /// <summary>是否请求服务方同时返回网页正文</summary>
        public bool IncludeContent { get; init; }

        /// <summary>单条正文的最大字符数，&lt;= 0 表示不截断</summary>
        public int MaxContentChars { get; init; } = 2000;

        /// <summary>结果语言偏好（仅部分服务方支持，如 ISO 639-1 "zh"）</summary>
        public string? Language { get; init; }

        /// <summary>结果地区偏好（仅部分服务方支持，如 "CN"）</summary>
        public string? Country { get; init; }

        /// <summary>触发搜索的原始用户问题（部分服务方用于理解搜索意图）</summary>
        public string? OriginalQuestion { get; init; }
    }

    /// <summary>单条搜索结果</summary>
    public sealed class WebSearchResultItem
    {
        public string Title { get; init; } = "";
        public string Url { get; init; } = "";

        /// <summary>短摘要</summary>
        public string? Snippet { get; init; }

        /// <summary>网页正文（仅在请求正文且服务方支持时非空）</summary>
        public string? Content { get; init; }

        /// <summary>发布时间（服务方原样返回的字符串）</summary>
        public string? PublishedDate { get; init; }

        /// <summary>站点名 / 来源</summary>
        public string? Source { get; init; }

        /// <summary>服务方给出的相关性分数（不同服务方量纲不同）</summary>
        public double? Score { get; init; }
    }

    /// <summary>搜索结果集。失败时 Success=false，Error 为用户可读信息，ErrorDetails 为原始错误内容。</summary>
    public sealed class WebSearchResponse
    {
        public bool Success { get; private init; }
        public string Error { get; private init; } = "";
        public string? ErrorDetails { get; private init; }
        public List<WebSearchResultItem> Results { get; private init; } = [];

        public static WebSearchResponse Ok(List<WebSearchResultItem> results) =>
            new() { Success = true, Results = results };

        public static WebSearchResponse Fail(string error, string? details = null) =>
            new() { Success = false, Error = error, ErrorDetails = details };
    }

    /// <summary>单个网页的抓取结果</summary>
    public sealed class WebFetchResponse
    {
        public bool Success { get; private init; }
        public string Error { get; private init; } = "";
        public string? ErrorDetails { get; private init; }
        public string Url { get; private init; } = "";
        public string? Title { get; private init; }
        public string? Content { get; private init; }

        public static WebFetchResponse Ok(string url, string? title, string? content) =>
            new() { Success = true, Url = url, Title = title, Content = content };

        public static WebFetchResponse Fail(string error, string? details = null, string url = "") =>
            new() { Success = false, Error = error, ErrorDetails = details, Url = url };
    }

    /// <summary>提供方解析失败原因</summary>
    public enum WebSearchResolutionError
    {
        None = 0,

        /// <summary>配置里的提供方不是具体服务方（FromSettings 等）</summary>
        UnknownProvider = 1,

        /// <summary>该提供方没有配置 API Key</summary>
        ApiKeyMissing = 2
    }

    /// <summary>提供方解析结果：要么带回可用实例，要么带回失败原因（由调用方本地化）。</summary>
    public sealed class WebSearchProviderResolution
    {
        public IWebSearchProvider? Provider { get; init; }
        public WebSearchResolutionError Error { get; init; } = WebSearchResolutionError.None;

        /// <summary>本次实际解析出的提供方（失败时为配置指定的那个，便于报错时说明）</summary>
        public WebSearchProviderKind ResolvedKind { get; init; }
    }
}
