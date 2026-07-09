namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点元数据，用于 UI 自动发现和渲染。
    /// 每个 IGenerationNode 实现都应标记此特性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class NodeInfoAttribute : Attribute
    {
        public string Label { get; }
        public string Icon { get; init; } = "●";
        public string Color { get; init; } = "#888";
        public string Category { get; init; } = "General";
        public string? Description { get; init; }

        public NodeInfoAttribute(string label) { Label = label; }
    }
}
