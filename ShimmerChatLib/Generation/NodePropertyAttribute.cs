namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点属性的显示元数据，用于 GenericNodeEditor 自动生成表单。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class NodePropertyAttribute : Attribute
    {
        /// <summary>显示名称</summary>
        public string Label { get; }

        /// <summary>提示文本</summary>
        public string? Hint { get; init; }

        /// <summary>排序 (越小越靠前)</summary>
        public int Order { get; init; }

        public NodePropertyAttribute(string label)
        {
            Label = label;
        }
    }
}
