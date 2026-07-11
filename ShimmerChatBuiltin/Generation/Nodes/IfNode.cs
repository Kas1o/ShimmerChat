using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Generation.Nodes
{
    /// <summary>
    /// 条件分支节点：如果 Condition 满足则执行 Then，否则执行 Else。
    /// Condition 目前仅支持 SharedState['key'] == "value"
    /// </summary>
    [NodeInfo("node.condition", Icon = "◇", Color = "#f0a040", CategoryKeys = ["category.flow", "category.branching"])]
    public class IfNode : IGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "If";

        /// <summary>
        /// 条件表达式，格式: SharedState['key'] == "value"
        /// </summary>
        [NodeProperty("prop.if_node.condition", HintKey = "prop.if_node.condition.hint")]
        public string Condition { get; set; } = "";

        [NodeProperty("prop.if_node.then")]
        public IGenerationNode? Then { get; set; }
        [NodeProperty("prop.if_node.else")]
        public IGenerationNode? Else { get; set; }

        public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
        {
            bool result = EvaluateCondition(context);

            if (result && Then != null)
            {
                var thenResult = await Then.ExecuteAsync(context);
                if (!thenResult.Success)
                {
                    thenResult.NodeId ??= Id;
                    thenResult.NodeName ??= Name;
                }
                return thenResult;
            }
            else if (!result && Else != null)
            {
                var elseResult = await Else.ExecuteAsync(context);
                if (!elseResult.Success)
                {
                    elseResult.NodeId ??= Id;
                    elseResult.NodeName ??= Name;
                }
                return elseResult;
            }

            return NodeResult.SuccessResult();
        }

        private bool EvaluateCondition(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(Condition))
                return false;

            // 简单条件解析: SharedState['key'] == "value"
            var cond = Condition.Trim();
            var eqIdx = cond.IndexOf("==", StringComparison.Ordinal);
            if (eqIdx < 0)
            {
                // 尝试 !=
                eqIdx = cond.IndexOf("!=", StringComparison.Ordinal);
                if (eqIdx < 0) return false;
                var leftNe = ParseKey(cond[..eqIdx].Trim());
                var rightNe = ParseValue(cond[(eqIdx + 2)..].Trim());
                var valNe = context.Env.Transient.SharedState.TryGetValue(leftNe, out var v) ? v?.ToString() : null;
                return !string.Equals(valNe, rightNe, StringComparison.OrdinalIgnoreCase);
            }

            var left = ParseKey(cond[..eqIdx].Trim());
            var right = ParseValue(cond[(eqIdx + 2)..].Trim());
            var val = context.Env.Transient.SharedState.TryGetValue(left, out var sv) ? sv?.ToString() : null;
            return string.Equals(val, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string ParseKey(string expr)
        {
            // SharedState['key'] → key
            const string prefix = "SharedState[";
            if (expr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && expr.EndsWith("]"))
            {
                var inner = expr[prefix.Length..^1].Trim();
                return inner.Trim('\'', '"');
            }
            return expr.Trim('\'', '"');
        }

        private static string ParseValue(string expr)
        {
            return expr.Trim('\'', '"');
        }
    }
}
