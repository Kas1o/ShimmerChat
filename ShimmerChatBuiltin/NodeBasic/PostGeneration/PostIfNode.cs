using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PostGeneration
{
    /// <summary>后处理条件分支可用的数据源。</summary>
    public enum PostConditionSource
    {
        /// <summary>本轮响应文本。</summary>
        ResponseText,
        /// <summary>本轮是否带工具调用（"true" / "false"）。</summary>
        HasToolCall,
        /// <summary>本轮第一个工具调用的名称（无调用时为空）。</summary>
        FirstToolName,
        /// <summary>跨轮共享状态。</summary>
        SharedState,
        /// <summary>KV 存储。</summary>
        KVData
    }

    /// <summary>
    /// 后处理条件分支。用于表达"按响应内容 / 按工具名决定是否继续循环"这类规则。
    /// </summary>
    [NodeInfo("node.post_condition",
        Icon = "◇",
        Color = "var(--node-branch)",
        CategoryKeys = ["category.post", "category.flow", "category.branching"],
        DescriptionKey = "node.post_condition.desc")]
    public class PostIfNode : IPostGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "Post If";

        [NodeProperty("prop.post_condition.source", HintKey = "prop.post_condition.source.hint", Order = 1)]
        public PostConditionSource Source { get; set; } = PostConditionSource.ResponseText;

        [NodeProperty("prop.post_condition.ss_key",
            HintKey = "prop.post_condition.ss_key.hint", Order = 2)]
        public string SharedStateKey { get; set; } = "";

        [NodeProperty("prop.post_condition.kv_collection",
            HintKey = "prop.post_condition.kv_collection.hint", Order = 3)]
        public string KVDataCollection { get; set; } = "";

        [NodeProperty("prop.post_condition.kv_key",
            HintKey = "prop.post_condition.kv_key.hint", Order = 4)]
        public string KVDataKey { get; set; } = "";

        [NodeProperty("prop.post_condition.operator",
            HintKey = "prop.post_condition.operator.hint", Order = 5)]
        public PostConditionOperator Operator { get; set; } = PostConditionOperator.Is;

        [NodeProperty("prop.post_condition.value",
            HintKey = "prop.post_condition.value.hint", Order = 6)]
        public string Value { get; set; } = "";

        [NodeProperty("prop.post_condition.then", Order = 7)]
        public IPostGenerationNode? Then { get; set; }

        [NodeProperty("prop.post_condition.else", Order = 8)]
        public IPostGenerationNode? Else { get; set; }

        public async Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context)
        {
            var left = ResolveSourceValue(context);
            if (left == null)
                return Else != null
                    ? await ExecuteChild(Else, context)
                    : PostNodeResult.SuccessResult();

            var matched = EvaluateOperator(left);

            if (matched && Then != null)
                return await ExecuteChild(Then, context);
            if (!matched && Else != null)
                return await ExecuteChild(Else, context);

            return PostNodeResult.SuccessResult();
        }

        private string? ResolveSourceValue(PostNodeExecutionContext context)
        {
            var env = context.Env;
            return Source switch
            {
                PostConditionSource.ResponseText => env.ResponseText,

                PostConditionSource.HasToolCall =>
                    env.ResponseMessage.toolCalls is { Count: > 0 } ? "true" : "false",

                PostConditionSource.FirstToolName =>
                    env.ResponseMessage.toolCalls is { Count: > 0 } calls ? calls[0].name : null,

                PostConditionSource.SharedState =>
                    env.SharedState.TryGetValue(SharedStateKey, out var value) ? value?.ToString() : null,

                PostConditionSource.KVData =>
                    env.Persistent.KVData.Read(KVDataCollection, KVDataKey),

                _ => null
            };
        }

        private bool EvaluateOperator(string leftValue)
        {
            var right = Value ?? "";

            if (double.TryParse(leftValue, out var l) && double.TryParse(right, out var r))
            {
                return Operator switch
                {
                    PostConditionOperator.Is => l == r,
                    PostConditionOperator.IsNot => l != r,
                    PostConditionOperator.Contains => l == r,
                    PostConditionOperator.NotContains => l != r,
                    _ => false
                };
            }

            return Operator switch
            {
                PostConditionOperator.Is =>
                    leftValue.Equals(right, StringComparison.OrdinalIgnoreCase),
                PostConditionOperator.Contains =>
                    leftValue.Contains(right, StringComparison.OrdinalIgnoreCase),
                PostConditionOperator.IsNot =>
                    !leftValue.Equals(right, StringComparison.OrdinalIgnoreCase),
                PostConditionOperator.NotContains =>
                    !leftValue.Contains(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private async Task<PostNodeResult> ExecuteChild(IPostGenerationNode child, PostNodeExecutionContext context)
        {
            var result = await child.ExecuteAsync(context);
            if (!result.Success)
            {
                result.NodeId ??= Id;
                result.NodeName ??= Name;
            }
            return result;
        }
    }

    public enum PostConditionOperator
    {
        Is,
        IsNot,
        Contains,
        NotContains
    }
}
