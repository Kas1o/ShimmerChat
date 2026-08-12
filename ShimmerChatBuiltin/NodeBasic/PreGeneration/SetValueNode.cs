using System.Globalization;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    /// <summary>
    /// 写入目标：SharedState（本次生成临时状态）或 KVData（持久化存储）
    /// </summary>
    public enum SetValueTarget
    {
        SharedState,
        KVData
    }

    /// <summary>
    /// SharedState 值类型
    /// </summary>
    public enum SetValueType
    {
        String,
        Int,
        Float,
        Bool
    }

    /// <summary>
    /// 设置值节点：将 Key/Value 写入 SharedState（本次生成内共享）或 KVData（持久保存）。
    /// </summary>
    [NodeInfo("node.set_value", Icon = "✎", Color = "var(--node-fragment)", CategoryKeys = ["category.variable"], DescriptionKey = "node.set_value.desc")]
    [NodeEditor(typeof(SetValueNodeEditor))]
    public class SetValueNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Set Value";

        [NodeProperty("prop.set_value.target", HintKey = "prop.set_value.target.hint", Order = 1)]
        public SetValueTarget Target { get; set; } = SetValueTarget.SharedState;

        [NodeProperty("prop.set_value.key", HintKey = "prop.set_value.key.hint", Order = 2)]
        public string Key { get; set; } = "";

        [NodeProperty("prop.set_value.value", HintKey = "prop.set_value.value.hint", MultiLine = true, Order = 3)]
        public string Value { get; set; } = "";

        [NodeProperty("prop.set_value.value_type", HintKey = "prop.set_value.value_type.hint", Order = 4)]
        public SetValueType ValueType { get; set; } = SetValueType.String;

        [NodeProperty("prop.set_value.kv_collection", HintKey = "prop.set_value.kv_collection.hint", Order = 5)]
        public string KVCollection { get; set; } = "";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(Key))
                return Task.FromResult(NodeResult.Failure(
                    NodeErrorCodes.DataMissing,
                    "SetValueNode: Key is empty.",
                    nodeId: Id, nodeName: Name));

            switch (Target)
            {
                case SetValueTarget.SharedState:
                    if (!TryConvertValue(Value, ValueType, out var converted, out var error))
                        return Task.FromResult(NodeResult.Failure(
                            NodeErrorCodes.ParseError, error, nodeId: Id, nodeName: Name));

                    context.Env.Transient.SharedState[Key] = converted;
                    break;

                case SetValueTarget.KVData:
                    if (string.IsNullOrWhiteSpace(KVCollection))
                        return Task.FromResult(NodeResult.Failure(
                            NodeErrorCodes.DataMissing,
                            "SetValueNode: KVCollection is empty.",
                            nodeId: Id, nodeName: Name));

                    context.Env.Persistent.KVData.Write(KVCollection, Key, Value ?? "");
                    break;

                default:
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ParseError,
                        $"SetValueNode: Unknown target '{Target}'.",
                        nodeId: Id, nodeName: Name));
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private static bool TryConvertValue(string? raw, SetValueType type, out object value, out string error)
        {
            value = default!;
            error = "";

            switch (type)
            {
                case SetValueType.String:
                    value = raw ?? "";
                    return true;

                case SetValueType.Int:
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    {
                        value = i;
                        return true;
                    }
                    break;

                case SetValueType.Float:
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    {
                        value = f;
                        return true;
                    }
                    break;

                case SetValueType.Bool:
                    if (bool.TryParse(raw, out var b))
                    {
                        value = b;
                        return true;
                    }
                    break;
            }

            error = $"SetValueNode: Cannot convert value '{raw}' to {type}.";
            return false;
        }
    }
}
