using System.Text;

namespace ShimmerChatBuiltin.WebSearch
{
    /// <summary>
    /// 搜索结果 → 模型可读文本。节点注入上下文与工具返回结果共用同一格式，
    /// 保证两种用法下模型看到的结构一致。
    /// </summary>
    public static class WebSearchFormatter
    {
        /// <summary>
        /// 搜索结果块。第一行为查询与提供方信息，随后是逐条结果。
        /// </summary>
        public static string FormatResults(
            WebSearchResponse response, string query, string providerDisplayName, int maxContentChars)
        {
            var builder = new StringBuilder();
            builder.Append("Search results for \"")
                .Append(query)
                .Append("\" via ")
                .Append(providerDisplayName)
                .Append(" — ")
                .Append(response.Results.Count)
                .Append(" result(s)");

            if (response.Results.Count == 0)
                return builder.AppendLine().Append("(no results returned)").ToString().TrimEnd();

            builder.AppendLine();

            var index = 0;
            foreach (var item in response.Results)
            {
                index++;
                builder.AppendLine();
                builder.Append('[').Append(index).Append("] ").AppendLine(string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title);

                if (!string.IsNullOrWhiteSpace(item.Url))
                    builder.Append("URL: ").AppendLine(item.Url);
                if (!string.IsNullOrWhiteSpace(item.PublishedDate))
                    builder.Append("Published: ").AppendLine(item.PublishedDate);
                if (!string.IsNullOrWhiteSpace(item.Source))
                    builder.Append("Source: ").AppendLine(item.Source);

                var snippet = Truncate(item.Snippet, maxContentChars);
                if (!string.IsNullOrWhiteSpace(snippet))
                    builder.Append("Snippet: ").AppendLine(snippet);

                var content = Truncate(item.Content, maxContentChars);
                if (!string.IsNullOrWhiteSpace(content))
                    builder.Append("Content: ").AppendLine(content);
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>单页抓取结果块</summary>
        public static string FormatFetch(WebFetchResponse response, int maxChars)
        {
            var builder = new StringBuilder();
            builder.Append("Page content: ").AppendLine(response.Url);

            if (!string.IsNullOrWhiteSpace(response.Title))
                builder.Append("Title: ").AppendLine(response.Title);

            builder.AppendLine();
            builder.AppendLine(Truncate(response.Content, maxChars) ?? "(empty page content)");

            return builder.ToString().TrimEnd();
        }

        private static string? Truncate(string? text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (maxChars <= 0 || text.Length <= maxChars) return text;
            return text[..maxChars] + $"…(truncated, {text.Length} chars total)";
        }
    }
}
