using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    public enum ConditionSource
    {
        KVData,
        LastMessage,
        AllMessages,
        SharedState
    }

    public enum ConditionOperator
    {
        Is,
        Contains,
        IsNot,
        NotContains
    }

    public enum OnConvertFailBehavior
    {
        Failure,
        AsFalse
    }

    /// <summary>
    /// 简单条件分支节点：选择数据源 → 选运算符 → 填比较值。
    /// 支持 KVData / LastMessage / AllMessages / SharedState 四种数据源。
    /// </summary>
    [NodeInfo("node.condition", Icon = "◇", Color = "var(--node-branch)", CategoryKeys = ["category.flow", "category.branching"])]
    [NodeEditor(typeof(IfNodeEditor))]
    public class IfNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "If";

        [NodeProperty("prop.if_node.source", HintKey = "prop.if_node.source.hint", Order = 1)]
        public ConditionSource Source { get; set; } = ConditionSource.KVData;

        [NodeProperty("prop.if_node.kv_collection", HintKey = "prop.if_node.kv_collection.hint", Order = 2)]
        public string KVDataCollection { get; set; } = "";

        [NodeProperty("prop.if_node.kv_key", HintKey = "prop.if_node.kv_key.hint", Order = 3)]
        public string KVDataKey { get; set; } = "";

        [NodeProperty("prop.if_node.ss_key", HintKey = "prop.if_node.ss_key.hint", Order = 4)]
        public string SharedStateKey { get; set; } = "";

        [NodeProperty("prop.if_node.operator", HintKey = "prop.if_node.operator.hint", Order = 5)]
        public ConditionOperator Operator { get; set; } = ConditionOperator.Is;

        [NodeProperty("prop.if_node.value", HintKey = "prop.if_node.value.hint", Order = 6)]
        public string Value { get; set; } = "";

        [NodeProperty("prop.if_node.on_convert_fail", HintKey = "prop.if_node.on_convert_fail.hint", Order = 7)]
        public OnConvertFailBehavior OnConvertFail { get; set; } = OnConvertFailBehavior.AsFalse;

        [NodeProperty("prop.if_node.then", Order = 8)]
        public IPreGenerationNode? Then { get; set; }

        [NodeProperty("prop.if_node.else", Order = 9)]
        public IPreGenerationNode? Else { get; set; }

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            string? leftValue = ResolveSourceValue(context);

            if (leftValue == null)
            {
                if (OnConvertFail == OnConvertFailBehavior.Failure)
                    return NodeResult.Failure(
                        NodeErrorCodes.DataMissing,
                        $"IfNode: source '{Source}' returned null or empty.",
                        nodeId: Id, nodeName: Name);

                return Else != null
                    ? await ExecuteChild(Else, context)
                    : NodeResult.SuccessResult();
            }

            bool result = EvaluateOperator(leftValue);

            if (result && Then != null)
                return await ExecuteChild(Then, context);
            if (!result && Else != null)
                return await ExecuteChild(Else, context);

            return NodeResult.SuccessResult();
        }

        private string? ResolveSourceValue(PreNodeExecutionContext context)
        {
            return Source switch
            {
                ConditionSource.KVData =>
                    context.Env.Persistent.KVData.Read(KVDataCollection, KVDataKey),

                ConditionSource.LastMessage =>
                    context.Env.Transient.Fragments.LastOrDefault()?.Message.Content,

                ConditionSource.AllMessages =>
                    context.Env.Transient.Fragments.Count == 0 ? null
                        : string.Join("\n", context.Env.Transient.Fragments.Select(f => f.Message.Content)),

                ConditionSource.SharedState =>
                    context.Env.Transient.SharedState.TryGetValue(SharedStateKey, out var sv)
                        ? sv?.ToString() : null,

                _ => null
            };
        }

        private bool EvaluateOperator(string leftValue)
        {
            var right = Value ?? "";

            // 优先尝试数值比较
            if (double.TryParse(leftValue, out var l) && double.TryParse(right, out var r))
            {
                return Operator switch
                {
                    ConditionOperator.Is => l == r,
                    ConditionOperator.IsNot => l != r,
                    ConditionOperator.Contains => l == r,
                    ConditionOperator.NotContains => l != r,
                    _ => false
                };
            }

            // 字符串比较
            return Operator switch
            {
                ConditionOperator.Is =>
                    leftValue.Equals(right, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.Contains =>
                    leftValue.Contains(right, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.IsNot =>
                    !leftValue.Equals(right, StringComparison.OrdinalIgnoreCase),
                ConditionOperator.NotContains =>
                    !leftValue.Contains(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private async Task<NodeResult> ExecuteChild(IPreGenerationNode child, PreNodeExecutionContext context)
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
}
