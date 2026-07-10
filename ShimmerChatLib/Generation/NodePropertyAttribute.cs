namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点属性的显示元数据，用于 GenericNodeEditor 自动生成表单。
    /// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class NodePropertyAttribute : Attribute
    {
        /// <summary>属性标签本地化 Key</summary>
        public string LabelKey { get; }

        /// <summary>提示文本本地化 Key</summary>
        public string? HintKey { get; init; }

        /// <summary>排序 (越小越靠前)</summary>
        public int Order { get; init; }

        public NodePropertyAttribute(string labelKey)
        {
            LabelKey = labelKey;
        }
    }
}
