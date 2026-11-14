using System;
using System.Collections.Generic;
using System.Linq;

namespace ShimmerChatLib
{
    /// <summary>
    /// 智能体列表的排序方式。持久化时使用枚举名，避免与界面文案耦合。
    /// </summary>
    public enum AgentSortMode
    {
        Default,
        NameAscending,
        NameDescending,
        ChatsDescending,
        ChatsAscending,
        TagsDescending,
    }

    /// <summary>标签及其出现次数，用于筛选栏展示。</summary>
    public sealed record AgentTagCount(string Tag, int Count);

    /// <summary>
    /// 标签筛选方向。<see cref="Include"/> 保留命中标签的智能体；
    /// <see cref="Exclude"/> 反向筛选，隐藏命中标签的智能体。
    /// </summary>
    public enum AgentTagMatchMode
    {
        Include,
        Exclude,
    }

    /// <summary>
    /// 智能体列表的检索、筛选与排序逻辑。
    /// 抽离自界面，便于单元测试并保证列表行为只有一份实现。
    /// </summary>
    public static class AgentListQuery
    {
        /// <summary>
        /// 解析持久化的排序方式。未知值返回 false，由调用方决定回退并记录告警。
        /// </summary>
        public static bool TryParseSortMode(string? value, out AgentSortMode mode)
        {
            if (Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode))
                return true;

            mode = AgentSortMode.Default;
            return false;
        }

        /// <summary>
        /// 解析持久化的标签筛选方向。未知值返回 false，由调用方决定回退并记录告警。
        /// </summary>
        public static bool TryParseTagMatchMode(string? value, out AgentTagMatchMode mode)
        {
            if (Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode))
                return true;

            mode = AgentTagMatchMode.Include;
            return false;
        }

        /// <summary>
        /// 按搜索词与标签筛选，并按指定方式排序。
        /// 多个标签之间是「或」关系：包含模式命中任意一个即可，排除模式命中任意一个即隐藏。
        /// </summary>
        public static List<Agent> FilterAndSort(
            IEnumerable<Agent> agents,
            string? search,
            IEnumerable<string>? tags,
            AgentSortMode sortMode,
            AgentTagMatchMode tagMatchMode = AgentTagMatchMode.Include)
        {
            ArgumentNullException.ThrowIfNull(agents);

            IEnumerable<Agent> result = agents;

            var query = search?.Trim();
            if (!string.IsNullOrEmpty(query))
                result = result.Where(agent => MatchesSearch(agent, query));

            var tagSet = BuildTagSet(tags);
            if (tagSet.Count > 0)
            {
                result = tagMatchMode == AgentTagMatchMode.Exclude
                    ? result.Where(agent => !HasAnySelectedTag(agent, tagSet))
                    : result.Where(agent => HasAnySelectedTag(agent, tagSet));
            }

            return sortMode switch
            {
                AgentSortMode.NameAscending => result.OrderBy(agent => agent.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
                AgentSortMode.NameDescending => result.OrderByDescending(agent => agent.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList(),
                AgentSortMode.ChatsDescending => result.OrderByDescending(GetChatCount).ToList(),
                AgentSortMode.ChatsAscending => result.OrderBy(GetChatCount).ToList(),
                AgentSortMode.TagsDescending => result.OrderByDescending(agent => agent.Tags?.Count ?? 0).ToList(),
                _ => result.ToList(),
            };
        }

        /// <summary>
        /// 统计所有智能体出现过的标签（忽略大小写与首尾空白），
        /// 按出现次数降序、名称升序排列。
        /// </summary>
        public static IReadOnlyList<AgentTagCount> CollectTagCounts(IEnumerable<Agent> agents)
        {
            ArgumentNullException.ThrowIfNull(agents);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in agents)
            {
                if (agent.Tags == null)
                    continue;

                // 同一智能体里的重复标签只算一次，计数代表“有多少个智能体使用该标签”。
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawTag in agent.Tags)
                {
                    var tag = rawTag?.Trim();
                    if (string.IsNullOrEmpty(tag) || !seen.Add(tag))
                        continue;

                    counts[tag] = counts.GetValueOrDefault(tag) + 1;
                }
            }

            return counts
                .Select(pair => new AgentTagCount(pair.Key, pair.Value))
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesSearch(Agent agent, string query)
        {
            return Contains(agent.Name, query)
                || Contains(agent.UserIntro, query)
                || Contains(agent.Greeting, query)
                || (agent.Tags?.Any(tag => Contains(tag, query)) ?? false)
                || (agent.AlternativeGreetings?.Any(greeting => Contains(greeting, query)) ?? false);
        }

        private static bool Contains(string? value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnySelectedTag(Agent agent, HashSet<string> tagSet)
        {
            if (agent.Tags == null)
                return false;

            foreach (var rawTag in agent.Tags)
            {
                var tag = rawTag?.Trim();
                if (!string.IsNullOrEmpty(tag) && tagSet.Contains(tag))
                    return true;
            }

            return false;
        }

        private static HashSet<string> BuildTagSet(IEnumerable<string>? tags)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tags == null)
                return set;

            foreach (var tag in tags)
            {
                var normalized = tag?.Trim();
                if (!string.IsNullOrEmpty(normalized))
                    set.Add(normalized);
            }

            return set;
        }

        private static int GetChatCount(Agent agent) => agent.ChatGuids?.Count ?? 0;
    }
}
