using System.Text;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>MCP 资源 → 上下文片段的渲染与预算控制。</summary>
    public static class McpResources
    {
        /// <summary>单个资源注入时的最大字符数，避免一个巨型资源吃掉整段上下文。</summary>
        public const int PerResourceCharLimit = 16000;

        /// <summary>
        /// 把资源解析结果渲染为注入文本。
        /// 同步解析出的内容原样返回；需要网络读取的资源由调用方先解析再传入。
        /// </summary>
        public static string Render(string endpointName, string endpointId, string uri, string? name, string content)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### Resource: {name ?? uri}");
            sb.AppendLine($"- endpoint: {endpointName} ({endpointId})");
            sb.AppendLine($"- uri: {uri}");
            sb.AppendLine();
            sb.AppendLine(content);
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 按总预算拼接多个已渲染的资源块。
        /// 超出预算时保留前面（按调用顺序）的块，并在末尾标注被省略的块数与 URI。
        /// </summary>
        public static (string Text, int IncludedCount, int SkippedCount) Compose(
            IReadOnlyList<(string Uri, string Rendered)> blocks, int budget)
        {
            if (blocks.Count == 0) return ("", 0, 0);

            var included = new List<string>();
            var skipped = new List<string>();
            var used = 0;

            foreach (var (uri, rendered) in blocks)
            {
                var cost = rendered.Length + 2;
                if (budget > 0 && used + cost > budget)
                {
                    skipped.Add(uri);
                    continue;
                }

                included.Add(rendered);
                used += cost;
            }

            var sb = new StringBuilder();
            if (included.Count > 0)
                sb.AppendLine(string.Join("\n\n", included));

            if (skipped.Count > 0)
            {
                sb.AppendLine(
                    $"[{skipped.Count} resource(s) omitted: the configured resource character budget ({budget}) was exhausted. " +
                    $"Omitted: {string.Join(", ", skipped)}]");
            }

            return (sb.ToString().TrimEnd(), included.Count, skipped.Count);
        }

        /// <summary>截断单个资源内容并标注。</summary>
        public static string Truncate(string content, int limit, string uri)
        {
            if (limit <= 0 || content.Length <= limit) return content;
            return content[..limit] + $"\n[...truncated: resource '{uri}' exceeded {limit} characters]";
        }
    }
}
