namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点属性的显示元数据，用于 GenericNodeEditor 自动生成表单。
    /// 所有 *Key 属性均为本地化 Key，由 LocService 解析为显示字符串。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class NodePropertyAttribute : Attribute
    {
        /// <summary>空值选项使用的本地化 Key（下拉框总保留一个空值项）。</summary>
        public const string DefaultOptionLabelKey = "node_editor.option_default";

        /// <summary>属性标签本地化 Key</summary>
        public string LabelKey { get; }

        /// <summary>提示文本本地化 Key</summary>
        public string? HintKey { get; init; }

        /// <summary>排序 (越小越靠前)</summary>
        public int Order { get; init; }

        /// <summary>标记字符串属性使用多行文本框编辑</summary>
        public bool MultiLine { get; init; }

        /// <summary>
        /// 动态选项来源的 Key。设置后属性渲染为下拉选择器，选项由
        /// <see cref="INodePropertyOptionsProvider"/> 提供。
        /// </summary>
        public string? OptionsKey { get; init; }

        /// <summary>
        /// 是否允许在选择界面搜索。只有在选项数达到
        /// <see cref="NodePropertyOptions.SearchThreshold"/> 时才真正显示搜索框，
        /// 小列表保持紧凑，避免编辑器界面被搜索框撑臃肿。
        /// </summary>
        public bool OptionsSearchable { get; init; }

        /// <summary>
        /// 是否在选项列表里显示 <see cref="NodePropertyOption.Description"/>。
        /// 默认关闭：描述只对需要副标题的列表（如工具、子智能体配置）打开。
        /// </summary>
        public bool ShowOptionDescription { get; init; }

        /// <summary>
        /// 空值选项的本地化 Key。不同属性的空值语义不同（未选择 / 使用默认 / 全部 / 继承），
        /// 默认沿用 <see cref="DefaultOptionLabelKey"/>。
        /// </summary>
        public string OptionsDefaultLabelKey { get; init; } = DefaultOptionLabelKey;

        public NodePropertyAttribute(string labelKey)
        {
            LabelKey = labelKey;
        }
    }

    /// <summary>
    /// 节点属性下拉框的一个选项。
    /// </summary>
    /// <param name="Value">写回属性的值。</param>
    /// <param name="Label">显示文本（提供者自行本地化）。</param>
    /// <param name="Group">分级展示用的分组名，留空表示不分组；支持 "父 / 子" 这样的多级文本。</param>
    /// <param name="Description">可选的副标题，仅在 <see cref="NodePropertyAttribute.ShowOptionDescription"/> 打开时显示。</param>
    /// <param name="SearchTerms">额外搜索词：只参与搜索匹配，不参与显示（例如别名、模型名、内部类型名）。</param>
    public sealed record NodePropertyOption(
        string Value,
        string Label,
        string? Group = null,
        string? Description = null,
        IReadOnlyList<string>? SearchTerms = null);

    /// <summary>
    /// <see cref="NodePropertyOption"/> 的搜索匹配规则，集中一处，避免 UI 与提供者各自实现。
    /// </summary>
    public static class NodePropertyOptions
    {
        /// <summary>选项数量达到该阈值才显示搜索框（即使属性声明了 OptionsSearchable）。</summary>
        public const int SearchThreshold = 8;

        /// <summary>查询串是否命中该选项：label / value / group / description / 额外搜索词任一包含即命中。</summary>
        public static bool Matches(NodePropertyOption option, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var q = query.Trim();
            if (Contains(option.Label, q) || Contains(option.Value, q)
                || Contains(option.Group, q) || Contains(option.Description, q))
                return true;

            if (option.SearchTerms is null)
                return false;

            foreach (var term in option.SearchTerms)
            {
                if (Contains(term, q))
                    return true;
            }

            return false;
        }

        /// <summary>按查询串过滤选项，保持提供者给出的原顺序。</summary>
        public static IReadOnlyList<NodePropertyOption> Filter(
            IReadOnlyList<NodePropertyOption> options, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return options;

            return options.Where(o => Matches(o, query)).ToList();
        }

        private static bool Contains(string? text, string query) =>
            !string.IsNullOrEmpty(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

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
