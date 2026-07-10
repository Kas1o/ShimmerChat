namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点元数据，用于 UI 自动发现和渲染。
    /// 每个 IGenerationNode 实现都应标记此特性。
    /// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class NodeInfoAttribute : Attribute
    {
        public string LabelKey { get; }
        public string Icon { get; init; } = "●";
        public string Color { get; init; } = "#888";
        public string[] CategoryKeys { get; init; } = ["category.general"];
        public string? DescriptionKey { get; init; }

        public NodeInfoAttribute(string labelKey) { LabelKey = labelKey; }
    }
}
