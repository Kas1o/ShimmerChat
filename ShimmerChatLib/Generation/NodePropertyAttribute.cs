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

        /// <summary>标记字符串属性使用多行文本框编辑</summary>
        public bool MultiLine { get; init; }

        /// <summary>
        /// 动态选项来源的 Key。设置后属性渲染为下拉框，选项由
        /// <see cref="INodePropertyOptionsProvider"/> 提供。
        /// </summary>
        public string? OptionsKey { get; init; }

        public NodePropertyAttribute(string labelKey)
        {
            LabelKey = labelKey;
        }
    }

    /// <summary>节点属性下拉框的一个选项。</summary>
    public sealed record NodePropertyOption(string Value, string Label);

    /// <summary>
    /// 节点属性的动态选项来源。实现会被插件加载器自动发现，
    /// 通过 <see cref="NodePropertyAttribute.OptionsKey"/> 关联到具体属性。
    /// </summary>
    public interface INodePropertyOptionsProvider
    {
        /// <summary>与 OptionsKey 对应的唯一标识。</summary>
        string Key { get; }

        /// <summary>返回当前可选项。</summary>
        IReadOnlyList<NodePropertyOption> GetOptions(IServiceProvider services);
    }
}
