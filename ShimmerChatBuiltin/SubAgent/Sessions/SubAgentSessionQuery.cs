namespace ShimmerChatBuiltin.SubAgent.Sessions
{
    /// <summary>会话列表排序方式。</summary>
    public enum SubAgentSessionSort
    {
        /// <summary>按最后更新时间倒序（默认，最近的会话在前）。</summary>
        UpdatedDesc,
        /// <summary>按创建时间倒序。</summary>
        CreatedDesc,
        /// <summary>按消息数倒序（找长会话）。</summary>
        MessageCountDesc,
        /// <summary>按标题升序。</summary>
        TitleAsc
    }

    /// <summary>会话索引的状态计数，用于筛选按钮上的数量徽标。</summary>
    public sealed record SubAgentSessionStatusCounts(
        int Total, int Running, int Completed, int Failed);

    /// <summary>
    /// 一个 SubAgent 配置分组。列表只渲染 <see cref="Sessions"/>（已筛选、已排序），
    /// <see cref="TotalCount"/> 保留该配置下的会话总数，用于「显示更多」与删除确认。
    /// </summary>
    public sealed class SubAgentSessionGroupView
    {
        public Guid ConfigGuid { get; init; }
        public string ConfigName { get; init; } = "";
        public int TotalCount { get; init; }
        public IReadOnlyList<SubAgentSessionSummary> Sessions { get; init; } = [];
    }

    /// <summary>
    /// 会话列表的纯逻辑：关键词 / 状态筛选、排序、分组，以及索引快照比较。
    /// <para>
    /// 与界面解耦，便于在大量会话下单独验证筛选与排序语义；界面只负责渲染结果。
    /// 索引条目已包含标题、预览与配置名，因此筛选完全在内存中进行，不额外读盘。
    /// </para>
    /// </summary>
    public static class SubAgentSessionQuery
    {
        /// <summary>匹配关键词的分隔符；所有词都必须命中（AND），大小写不敏感。</summary>
        private static readonly char[] KeywordSeparators = [' ', '\t', '\r', '\n'];

        /// <summary>
        /// 判断索引条目是否满足关键词与状态筛选。
        /// 关键词会匹配标题、最后预览、配置名，以及完整 / 无连字符的会话 Id。
        /// </summary>
        public static bool Matches(SubAgentSessionSummary summary, string? keyword, SubAgentSessionStatus? status)
        {
            if (status != null && summary.Status != status)
                return false;

            if (string.IsNullOrWhiteSpace(keyword))
                return true;

            var tokens = keyword.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return true;

            var id = summary.Id;
            var haystack = string.Join('\n',
                summary.Title,
                summary.LastPreview ?? "",
                summary.ConfigName,
                id.ToString("N"),
                id.ToString("D"));

            foreach (var token in tokens)
            {
                if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>按当前筛选条件与排序方式重新生成分组列表。</summary>
        public static List<SubAgentSessionGroupView> BuildGroups(
            IReadOnlyList<SubAgentSessionSummary> summaries,
            string? keyword,
            SubAgentSessionStatus? status,
            SubAgentSessionSort sort)
        {
            var groups = new List<SubAgentSessionGroupView>();
            if (summaries.Count == 0)
                return groups;

            foreach (var byConfig in summaries.GroupBy(s => s.ConfigGuid))
            {
                var all = byConfig.ToList();
                var matched = all
                    .Where(s => Matches(s, keyword, status))
                    .ToList();

                if (matched.Count == 0)
                    continue;

                // 配置改名后索引里可能混有新旧名称：展示最新的非空名称（all 已按更新时间倒序）。
                var configName = all
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(s => s.ConfigName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? byConfig.Key.ToString();

                SortInPlace(matched, sort);

                groups.Add(new SubAgentSessionGroupView
                {
                    ConfigGuid = byConfig.Key,
                    ConfigName = configName,
                    TotalCount = all.Count,
                    Sessions = matched
                });
            }

            return groups
                .OrderBy(g => g.ConfigName, StringComparer.CurrentCulture)
                .ThenBy(g => g.ConfigGuid)
                .ToList();
        }

        /// <summary>按给定排序方式就地排序（排序键相同时用更新时间与 Id 保证稳定）。</summary>
        public static void SortInPlace(List<SubAgentSessionSummary> sessions, SubAgentSessionSort sort)
        {
            Comparison<SubAgentSessionSummary> comparison = sort switch
            {
                SubAgentSessionSort.CreatedDesc => (a, b) =>
                {
                    var byCreated = b.CreatedAt.CompareTo(a.CreatedAt);
                    return byCreated != 0 ? byCreated : b.UpdatedAt.CompareTo(a.UpdatedAt);
                },
                SubAgentSessionSort.MessageCountDesc => (a, b) =>
                {
                    var byCount = b.MessageCount.CompareTo(a.MessageCount);
                    return byCount != 0 ? byCount : b.UpdatedAt.CompareTo(a.UpdatedAt);
                },
                SubAgentSessionSort.TitleAsc => (a, b) =>
                {
                    var byTitle = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                    return byTitle != 0 ? byTitle : b.UpdatedAt.CompareTo(a.UpdatedAt);
                },
                _ => (a, b) =>
                {
                    var byUpdated = b.UpdatedAt.CompareTo(a.UpdatedAt);
                    return byUpdated != 0 ? byUpdated : b.CreatedAt.CompareTo(a.CreatedAt);
                }
            };

            sessions.Sort(comparison);
        }

        /// <summary>统计各状态数量（使用未筛选的全量索引）。</summary>
        public static SubAgentSessionStatusCounts CountByStatus(IReadOnlyList<SubAgentSessionSummary> summaries)
        {
            var running = 0;
            var completed = 0;
            var failed = 0;
            foreach (var summary in summaries)
            {
                switch (summary.Status)
                {
                    case SubAgentSessionStatus.Running:
                        running++;
                        break;
                    case SubAgentSessionStatus.Completed:
                        completed++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }

            return new SubAgentSessionStatusCounts(summaries.Count, running, completed, failed);
        }

        /// <summary>
        /// 判断两份索引快照是否等价。等价时自动刷新不再触发渲染，
        /// 避免每几秒重新渲染整段 Markdown 记录。
        /// </summary>
        public static bool SummariesEqual(
            IReadOnlyList<SubAgentSessionSummary> left,
            IReadOnlyList<SubAgentSessionSummary> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                var a = left[i];
                var b = right[i];
                if (a.Id != b.Id
                    || a.ConfigGuid != b.ConfigGuid
                    || a.UpdatedAt != b.UpdatedAt
                    || a.CreatedAt != b.CreatedAt
                    || a.MessageCount != b.MessageCount
                    || a.Status != b.Status
                    || !string.Equals(a.Title, b.Title, StringComparison.Ordinal)
                    || !string.Equals(a.ConfigName, b.ConfigName, StringComparison.Ordinal)
                    || !string.Equals(a.LastPreview, b.LastPreview, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 会话详情是否需要重新读盘。
    /// <para>
    /// 索引条目在每次落盘时都会刷新 <see cref="SubAgentSessionSummary.UpdatedAt"/>，
    /// 因此用「索引快照 vs 已加载快照」即可判断详情是否可能过期，不必每个刷新周期都反序列化整段会话。
    /// </para>
    /// </summary>
    public static class SubAgentSessionRefreshPlan
    {
        /// <summary>
        /// <paramref name="current"/> 是索引里的最新条目，<paramref name="loaded"/> 是详情上次读取时的条目快照。
        /// 返回 true 表示详情需要重新读取。
        /// <para>
        /// <paramref name="hasLoadedSnapshot"/> 表示是否已经尝试读过详情（成功或失败都算）：
        /// 失败场景同样记录快照，避免坏数据每个刷新周期都重复报错；用户手动刷新时由调用方强制重读。
        /// </para>
        /// </summary>
        public static bool NeedsDetailReload(
            SubAgentSessionSummary? current, SubAgentSessionSummary? loaded, bool hasLoadedSnapshot)
        {
            if (!hasLoadedSnapshot || current == null || loaded == null)
                return true;

            return current.UpdatedAt != loaded.UpdatedAt
                || current.MessageCount != loaded.MessageCount
                || current.Status != loaded.Status
                || current.Title != loaded.Title
                || current.ConfigName != loaded.ConfigName;
        }
    }

    /// <summary>
    /// 会话详情的消息窗口：默认只渲染最近 N 条，向上翻时把窗口起始位置固定在
    /// 绝对下标上，这样自动刷新追加新消息时不会把用户正在阅读的内容挤走。
    /// </summary>
    public static class SubAgentTranscriptWindow
    {
        /// <summary>每次「显示更早」加载的条数，也是默认窗口大小。</summary>
        public const int PageSize = 50;

        /// <summary>
        /// 计算要渲染的窗口。返回的 Start 为绝对下标，Count 为条数；
        /// <paramref name="pinnedStart"/> 为 null 表示「跟随最新」。
        /// 固定的下标越界（会话被外部改写）时回退到「最近 N 条」。
        /// </summary>
        public static (int Start, int Count) Resolve(int total, int? pinnedStart)
        {
            if (total <= 0)
                return (0, 0);

            if (pinnedStart == null)
            {
                var tailStart = Math.Max(0, total - PageSize);
                return (tailStart, total - tailStart);
            }

            if (pinnedStart.Value < 0 || pinnedStart.Value >= total)
            {
                var tailStart = Math.Max(0, total - PageSize);
                return (tailStart, total - tailStart);
            }

            return (pinnedStart.Value, total - pinnedStart.Value);
        }

        /// <summary>「显示更早」后的新固定下标（0 表示已到第一条）。</summary>
        public static int ExpandEarlier(int total, int? pinnedStart)
        {
            var (start, _) = Resolve(total, pinnedStart);
            return Math.Max(0, start - PageSize);
        }
    }
}
