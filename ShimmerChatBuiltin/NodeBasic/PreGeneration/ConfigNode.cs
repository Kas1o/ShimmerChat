using System.Globalization;
using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
    /// <summary>
    /// 单个配置项：Name 用于 UI 展示，Key 是写入 SharedState 的键，
    /// ValueType 决定值的类型，DefaultValue 为默认值，Value 为配置模式下调整的值（留空则使用默认值）。
    /// </summary>
    public class ConfigItem
    {
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public SetValueType ValueType { get; set; } = SetValueType.String;
        public string DefaultValue { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// 配置节点：整体作为易调整的设置值节点，仅作用于 SharedState。
    /// 编辑模式定义配置项（名字 / SharedState 键 / 类型 / 默认值），配置模式调整每个项的值；
    /// 运行时将每个项的生效值（配置值优先，否则默认值）转换为对应类型后写入 SharedState。
    /// </summary>
    [NodeInfo("node.config", Icon = "⚙", Color = "var(--node-fragment)", CategoryKeys = ["category.variable"], DescriptionKey = "node.config.desc")]
    [NodeEditor(typeof(ConfigNodeEditor))]
    public class ConfigNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Config";

        /// <summary>
        /// 配置项列表的 JSON 序列化。以字符串存储以兼容节点序列化白名单：
        /// TypeNameHandling.Objects 会为 List 中的每个元素写入 $type，而白名单序列化器只允许节点类型，
        /// 因此不能直接使用 List&lt;ConfigItem&gt; 属性。
        /// </summary>
        public string ItemsJson { get; set; } = "[]";

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var loc = context.Env.Persistent.LocService;

            List<ConfigItem> items;
            if (string.IsNullOrWhiteSpace(ItemsJson))
            {
                items = new List<ConfigItem>();
            }
            else
            {
                try
                {
                    items = JsonConvert.DeserializeObject<List<ConfigItem>>(ItemsJson) ?? new List<ConfigItem>();
                }
                catch (Exception ex)
                {
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ParseError,
                        loc["node_err.config_parse_failed"],
                        ex.ToString(),
                        Id, Name));
                }
            }

            foreach (var item in items)
            {
                var label = string.IsNullOrWhiteSpace(item.Name) ? item.Key : item.Name;

                if (string.IsNullOrWhiteSpace(item.Key))
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.DataMissing,
                        loc.Format("node_err.config_empty_key", label),
                        nodeId: Id, nodeName: Name));

                var raw = string.IsNullOrEmpty(item.Value) ? item.DefaultValue : item.Value;
                if (!TryConvertValue(raw, item.ValueType, out var converted, out var error))
                    return Task.FromResult(NodeResult.Failure(
                        NodeErrorCodes.ParseError,
                        loc.Format("node_err.config_invalid_value", raw, LocTypeName(loc, item.ValueType), label),
                        error,
                        Id, Name));

                context.Env.Transient.SharedState[item.Key] = converted;
            }

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private static string LocTypeName(ShimmerChatLib.Interface.ILocService loc, SetValueType type)
        {
            return type switch
            {
                SetValueType.Int => loc["prop.set_value.value_type.int"],
                SetValueType.Float => loc["prop.set_value.value_type.float"],
                SetValueType.Bool => loc["prop.set_value.value_type.bool"],
                _ => loc["prop.set_value.value_type.string"]
            };
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

            error = $"ConfigNode: Cannot convert value '{raw}' to {type}.";
            return false;
        }
    }
}
