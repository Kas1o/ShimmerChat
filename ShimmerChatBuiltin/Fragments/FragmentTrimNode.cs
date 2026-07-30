using System.Collections.Concurrent;
using SharperLLM.Util;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Fragments
{
    /// <summary>
    /// Token 裁剪节点：超过 TokenBudget 时从最早的消息开始裁剪。
    /// 合并了旧的 TokenLimit 和 LatestN 功能。
    /// </summary>
    [NodeInfo("node.fragment_trim", Icon = "✂", Color = "var(--node-prompt)", CategoryKeys = ["category.content", "category.filter"])]
    public class FragmentTrimNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Fragment Trim";

        /// <summary>
        /// Token 预算，超过则从头部裁剪
        /// </summary>
        [NodeProperty("prop.fragment_trim.token_budget", HintKey = "prop.fragment_trim.token_budget.hint")]
        public int TokenBudget { get; set; } = 4096;

        /// <summary>
        /// 裁剪模式：TokenBudget（按 token 裁剪）或 LatestN（只保留最新 N 条）
        /// </summary>
        [NodeProperty("prop.fragment_trim.trim_mode", HintKey = "prop.fragment_trim.trim_mode.hint")]
        public TrimMode Mode { get; set; } = TrimMode.TokenBudget;

        /// <summary>
        /// LatestN 模式下保留的消息数
        /// </summary>
        [NodeProperty("prop.fragment_trim.count", HintKey = "prop.fragment_trim.count.hint")]
        public int Count { get; set; } = 10;

        /// <summary>
        /// 是否保留第一条 system 消息
        /// </summary>
        [NodeProperty("prop.fragment_trim.keep_first_system", HintKey = "prop.fragment_trim.keep_first_system.hint")]
        public bool KeepFirstSystem { get; set; } = true;

        /// <summary>
        /// Tokenizer 实例缓存（按解析后的绝对路径缓存），跨节点实例和生成会话共享。
        /// </summary>
        private static readonly ConcurrentDictionary<string, Tokenizers.HuggingFace.Tokenizer.Tokenizer> TokenizerCache = new();

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var fragments = context.Env.Transient.Fragments;

            if (fragments.Count == 0)
                return Task.FromResult(NodeResult.SuccessResult());

            bool hasFirstSystem = false;
            ContextSegment? firstSystem = null;

            if (KeepFirstSystem)
            {
                firstSystem = fragments.FirstOrDefault();
                hasFirstSystem = firstSystem != null && firstSystem.From == PromptBuilder.From.system;
            }

            if (Mode == TrimMode.LatestN)
            {
                TrimByCount(fragments, Count, hasFirstSystem, firstSystem);
                return Task.FromResult(NodeResult.SuccessResult());
            }
            else
            {
                return Task.FromResult(TrimByTokens(context, fragments, hasFirstSystem, firstSystem));
            }
        }

        private static void TrimByCount(List<ContextSegment> fragments, int count,
            bool hasFirstSystem, ContextSegment? firstSystem)
        {
            if (count >= fragments.Count)
                return;

            var kept = hasFirstSystem && firstSystem != null
                ? new List<ContextSegment> { firstSystem }
                : new List<ContextSegment>();

            var tail = fragments.TakeLast(count).ToList();
            if (hasFirstSystem && firstSystem != null && tail.Contains(firstSystem))
                tail.Remove(firstSystem);

            kept.AddRange(tail);

            // 清理孤儿 tool_result
            var orphanIds = FindOrphanToolResultIds(fragments, kept);
            kept.RemoveAll(s => s.From == PromptBuilder.From.tool_result
                && !string.IsNullOrEmpty(s.Message.id) && orphanIds.Contains(s.Message.id));

            fragments.Clear();
            fragments.AddRange(kept);
        }

        private NodeResult TrimByTokens(PreNodeExecutionContext context, List<ContextSegment> fragments,
            bool hasFirstSystem, ContextSegment? firstSystem)
        {
            var kvData = context.Env.Persistent.KVData;
            var rawPath = kvData.Read("Tokenize", "local_vocab_path");

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    "FragmentTrim: Tokenizer 路径未配置，请在 Tokenizer 配置面板中设置 Model 路径。",
                    nodeId: Id,
                    nodeName: Name);
            }

            string resolvedPath;
            if (Path.IsPathRooted(rawPath))
            {
                resolvedPath = rawPath;
            }
            else
            {
                resolvedPath = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, rawPath));
            }

            if (!File.Exists(resolvedPath))
            {
                return NodeResult.Failure(
                    NodeErrorCodes.ConfigNotFound,
                    $"FragmentTrim: Tokenizer 文件不存在: {resolvedPath}",
                    nodeId: Id,
                    nodeName: Name);
            }

            var tokenizer = TokenizerCache.GetOrAdd(resolvedPath, path =>
            {
                return Tokenizers.HuggingFace.Tokenizer.Tokenizer.FromFile(path);
            });

            var tokenCounts = fragments.Select(s =>
                tokenizer.Encode(s.Message.Content, true).FirstOrDefault()?.Ids?.Count ?? 0).ToList();

            var totalTokens = 0;
            var removeIndices = new HashSet<int>();
            for (int i = tokenCounts.Count - 1; i >= 0; i--)
            {
                totalTokens += tokenCounts[i];
                if (totalTokens > TokenBudget)
                {
                    if (hasFirstSystem && i == 0 && firstSystem != null)
                        break;
                    removeIndices.Add(i);
                }
            }

            // 收集被移除的 assistant 的 tool_call ids
            var orphanIds = new HashSet<string>();
            foreach (var i in removeIndices)
            {
                if (fragments[i].From == PromptBuilder.From.assistant
                    && fragments[i].Message.toolCalls != null)
                {
                    foreach (var tc in fragments[i].Message.toolCalls)
                    {
                        if (!string.IsNullOrEmpty(tc.id))
                            orphanIds.Add(tc.id);
                    }
                }
            }

            // 也移除孤儿 tool_result
            for (int i = fragments.Count - 1; i >= 0; i--)
            {
                if (fragments[i].From == PromptBuilder.From.tool_result
                    && !string.IsNullOrEmpty(fragments[i].Message.id)
                    && orphanIds.Contains(fragments[i].Message.id))
                {
                    removeIndices.Add(i);
                }
            }

            fragments.RemoveAll(s => removeIndices.Contains(fragments.IndexOf(s)));

            return NodeResult.SuccessResult();
        }

        private static HashSet<string> FindOrphanToolResultIds(
            List<ContextSegment> all, List<ContextSegment> kept)
        {
            var keptSet = new HashSet<ContextSegment>(kept);
            var orphanIds = new HashSet<string>();

            foreach (var seg in all.Except(keptSet))
            {
                if (seg.From == PromptBuilder.From.assistant && seg.Message.toolCalls != null)
                {
                    foreach (var tc in seg.Message.toolCalls)
                    {
                        if (!string.IsNullOrEmpty(tc.id))
                            orphanIds.Add(tc.id);
                    }
                }
            }
            return orphanIds;
        }
    }

    public enum TrimMode
    {
        TokenBudget,
        LatestN
    }
}
