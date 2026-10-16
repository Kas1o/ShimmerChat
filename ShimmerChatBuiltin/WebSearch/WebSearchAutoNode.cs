using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 自动联网搜索节点：按配置取出本次输入内容作为查询词（可选先让当前 API 改写成搜索词），
    /// 调用服务方搜索，并把结果作为片段追加到上下文末尾。
    /// 取不到查询词或搜索失败时按 <see cref="OnFailure"/> 明确失败或记录日志后跳过，绝不静默吞掉。
    /// </summary>
    [NodeInfo("node.web_search_auto", Icon = "🌐", Color = "var(--node-link)",
        CategoryKeys = ["category.content", "category.web_search"],
        DescriptionKey = "node.web_search_auto.desc")]
    public class WebSearchAutoNode : IPreGenerationNode
    {
        /// <summary>改写提示词中代表输入内容的占位符</summary>
        private const string InputPlaceholder = "{input}";

        internal const string DefaultRewritePrompt =
            "Rewrite the user's message into a single web search query.\n" +
            "Rules:\n" +
            "- Output only the query text: keywords, no quotes, no explanation, no trailing period.\n" +
            "- Keep names, versions, dates and other specific terms.\n" +
            "- Write the query in the same language as the user's message.\n\n" +
            "User message:\n" + InputPlaceholder + "\n\nSearch query:";

        internal const string DefaultHeader =
            "The following are live web search results. Answer the user's latest message using them " +
            "and cite the source URLs. If the results do not contain the answer, say so instead of guessing.";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Auto Web Search";

        /// <summary>
        /// 提供方解析入口。默认走真实工厂，测试可替换为桩，避免节点测试发起网络请求。
        /// </summary>
        internal Func<WebSearchConfig, WebSearchProviderKind, WebSearchProviderResolution> ProviderResolver { get; set; } =
            (config, kind) => WebSearchProviderFactory.Resolve(config, kind);

        /// <summary>使用的提供方</summary>
        [NodeProperty("prop.web_search.provider", HintKey = "prop.web_search.provider.hint", Order = 1)]
        public WebSearchProviderKind Provider { get; set; } = WebSearchProviderKind.FromSettings;

        /// <summary>查询词来源</summary>
        [NodeProperty("prop.web_search_auto.query_source", HintKey = "prop.web_search_auto.query_source.hint", Order = 2)]
        public WebSearchQuerySource QuerySource { get; set; } = WebSearchQuerySource.LastUserMessage;

        /// <summary>QuerySource = StaticText 时使用的固定查询词</summary>
        [NodeProperty("prop.web_search_auto.static_query", HintKey = "prop.web_search_auto.static_query.hint", Order = 3, MultiLine = true)]
        public string StaticQuery { get; set; } = "";

        /// <summary>是否先用当前 API 把输入改写成搜索词（会多一次模型调用）</summary>
        [NodeProperty("prop.web_search_auto.rewrite", HintKey = "prop.web_search_auto.rewrite.hint", Order = 4)]
        public bool RewriteQueryWithLlm { get; set; }

        /// <summary>改写提示词模板，必须包含 {input}</summary>
        [NodeProperty("prop.web_search_auto.rewrite_prompt", HintKey = "prop.web_search_auto.rewrite_prompt.hint", Order = 5, MultiLine = true)]
        public string RewritePrompt { get; set; } = DefaultRewritePrompt;

        /// <summary>查询词最大字符数（0 = 不截断）</summary>
        [NodeProperty("prop.web_search_auto.max_query_chars", HintKey = "prop.web_search_auto.max_query_chars.hint", Order = 6)]
        public int MaxQueryChars { get; set; } = 128;

        /// <summary>结果条数，0 表示使用全局配置</summary>
        [NodeProperty("prop.web_search_auto.max_results", HintKey = "prop.web_search_auto.max_results.hint", Order = 7)]
        public int MaxResults { get; set; }

        /// <summary>注入片段使用的角色</summary>
        [NodeProperty("prop.web_search_auto.role", HintKey = "prop.web_search_auto.role.hint", Order = 8)]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.system;

        /// <summary>结果块前面的说明文本，留空则只注入结果</summary>
        [NodeProperty("prop.web_search_auto.header", HintKey = "prop.web_search_auto.header.hint", Order = 9, MultiLine = true)]
        public string Header { get; set; } = DefaultHeader;

        /// <summary>取不到查询词 / 搜索失败时的行为</summary>
        [NodeProperty("prop.web_search_auto.on_failure", HintKey = "prop.web_search_auto.on_failure.hint", Order = 10)]
        public WebSearchFailureBehavior OnFailure { get; set; } = WebSearchFailureBehavior.FailGeneration;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;
            var kvData = context.Env.Persistent.KVData;

            var (config, configError) = WebSearchConfigManager.Load(kvData);
            if (config == null)
                return SkipOrFail(context, NodeErrorCodes.DataMissing,
                    loc["node_err.web_search_config_invalid"], configError);

            var (query, queryError) = ResolveQuery(context, loc);
            if (query == null)
                return SkipOrFail(context, NodeErrorCodes.DataMissing, queryError!, details: null);

            query = TruncateQuery(query);

            if (RewriteQueryWithLlm)
            {
                if (!RewritePrompt.Contains(InputPlaceholder))
                    return SkipOrFail(context, NodeErrorCodes.ParseError,
                        loc.Format("node_err.web_search_rewrite_placeholder_missing", InputPlaceholder), details: null);

                var api = context.Env.Transient.API;
                if (api == null)
                    return SkipOrFail(context, NodeErrorCodes.ApiUnavailable,
                        loc["node_err.web_search_no_api"], details: null);

                var (rewritten, rewriteError, rewriteDetails) = await RewriteQueryAsync(api.ChatClient, loc, query);
                if (rewritten == null)
                    return SkipOrFail(context, NodeErrorCodes.ServiceError, rewriteError!, rewriteDetails);

                query = TruncateQuery(rewritten);
            }

            var resolution = ProviderResolver(config, Provider);
            if (resolution.Provider == null)
                return SkipOrFail(context, NodeErrorCodes.ConfigNotFound,
                    DescribeResolutionError(loc, resolution), details: null);

            var request = new WebSearchQuery
            {
                Query = query,
                MaxResults = MaxResults > 0 ? MaxResults : config.MaxResults,
                IncludeContent = config.IncludeContent,
                MaxContentChars = config.MaxContentChars,
                Language = string.IsNullOrWhiteSpace(config.Language) ? null : config.Language.Trim(),
                Country = string.IsNullOrWhiteSpace(config.Country) ? null : config.Country.Trim(),
                OriginalQuestion = QuerySource == WebSearchQuerySource.LastUserMessage ? query : null
            };

            var response = await resolution.Provider.SearchAsync(request, context.CancellationToken);
            if (!response.Success)
                return SkipOrFail(context, NodeErrorCodes.ServiceError,
                    loc.Format("node_err.web_search_failed", resolution.Provider.DisplayName, response.Error),
                    response.ErrorDetails);

            if (response.Results.Count == 0)
            {
                // 搜索正常但无结果：不注入空片段，记录后可继续生成
                WriteLog(context, $"[{Name}] search for \"{query}\" returned no results; nothing injected.");
                return NodeResult.SuccessResult();
            }

            var block = WebSearchFormatter.FormatResults(
                response, query, resolution.Provider.DisplayName, config.MaxContentChars);

            if (!string.IsNullOrWhiteSpace(Header))
                block = Header.Trim() + Environment.NewLine + Environment.NewLine + block;

            context.Env.Transient.Fragments.Add(new ContextSegment
            {
                SourceType = typeof(WebSearchAutoNode),
                Message = new ChatMessage { Content = block },
                From = From,
                Metadata = new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["provider"] = resolution.Provider.DisplayName,
                    ["resultCount"] = response.Results.Count
                }
            });

            return NodeResult.SuccessResult();
        }

        /// <summary>取出查询词：最近一条用户消息，或节点配置的固定文本</summary>
        private (string? Query, string? Error) ResolveQuery(PreNodeExecutionContext context, ILocService loc)
        {
            if (QuerySource == WebSearchQuerySource.StaticText)
            {
                if (string.IsNullOrWhiteSpace(StaticQuery))
                    return (null, loc["node_err.web_search_static_query_empty"]);

                return (StaticQuery.Trim(), null);
            }

            var message = context.Env.Persistent.Chat.Messages
                .LastOrDefault(m => string.Equals(m.sender, Sender.User, StringComparison.OrdinalIgnoreCase)
                                    && m.GenerationState != MessageGenerationState.Regenerating);

            var content = message?.message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return (null, loc["node_err.web_search_no_user_input"]);

            return (content.Trim(), null);
        }

        /// <summary>
        /// 用当前 API 把输入改写成搜索词。改写走 Transient.API，因此需要树上已有 APISelectNode。
        /// </summary>
        private async Task<(string? Query, string? Error, string? Details)> RewriteQueryAsync(
            SharperLLM.API.IChatCompletionClient api, ILocService loc, string input)
        {
            var prompt = RewritePrompt.Replace(InputPlaceholder, input);

            try
            {
                var builder = new PromptBuilder
                {
                    Messages = [(new ChatMessage { Content = prompt }, PromptBuilder.From.user)]
                };

                var response = await api.GenerateAsync(builder);
                var query = NormalizeQuery(response.Body.Content);
                if (string.IsNullOrWhiteSpace(query))
                    return (null, loc["node_err.web_search_rewrite_empty"], null);

                return (query, null, null);
            }
            catch (Exception ex)
            {
                return (null, loc.Format("node_err.web_search_rewrite_failed", ex.Message), ex.ToString());
            }
        }

        /// <summary>清掉模型可能带上的引号、换行与结尾标点</summary>
        private static string NormalizeQuery(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var query = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

            // 模型可能同时用引号和句号包裹（如 "\"query\"."），两种都要反复剥掉
            while (true)
            {
                var stripped = query
                    .Trim('"', '\'', '`', '“', '”', '‘', '’')
                    .Trim()
                    .TrimEnd('.', '。', '!', '！', '?', '？', ':', '：')
                    .Trim();

                if (stripped == query) break;
                query = stripped;
            }

            return query;
        }

        /// <summary>按 MaxQueryChars 截断（尽量截在词边界），避免超出服务方的查询词长度限制</summary>
        private string TruncateQuery(string query)
        {
            if (MaxQueryChars <= 0 || query.Length <= MaxQueryChars)
                return query;

            var cut = query[..MaxQueryChars];
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > MaxQueryChars / 2)
                cut = cut[..lastSpace];

            return cut.TrimEnd();
        }

        private string DescribeResolutionError(ILocService loc, WebSearchProviderResolution resolution)
        {
            var name = WebSearchProviderCatalog.Find(resolution.ResolvedKind)?.DisplayName
                       ?? resolution.ResolvedKind.ToString();

            return resolution.Error switch
            {
                WebSearchResolutionError.ApiKeyMissing => loc.Format("node_err.web_search_api_key_missing", name),
                WebSearchResolutionError.UnknownProvider => loc.Format("node_err.web_search_unknown_provider", name),
                _ => loc["node_err.web_search_provider_unresolved"]
            };
        }

        /// <summary>按 OnFailure 策略处理：要么把失败向上汇报，要么记录日志后跳过本次注入</summary>
        private NodeResult SkipOrFail(
            PreNodeExecutionContext context, string code, string message, string? details)
        {
            if (OnFailure == WebSearchFailureBehavior.SkipAndLog)
            {
                WriteLog(context, $"[{Name}] skipped ({code}): {message}"
                    + (string.IsNullOrWhiteSpace(details) ? "" : $" | {details}"));
                return NodeResult.SuccessResult();
            }

            return NodeResult.Failure(code, message, details, Id, Name);
        }

        private void WriteLog(PreNodeExecutionContext context, string message) =>
            context.Env.Persistent.DebugOutput.Write("WebSearchAutoNode", "WebSearch", message);
    }
}
