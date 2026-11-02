namespace ShimmerChatLib.Generation.ToolUi
{
    /// <summary>声明式表单的字段类型。新增值时必须保证旧渲染器能降级显示（只读 + 自由文本）。</summary>
    public enum ToolUiFieldKind
    {
        /// <summary>单行文本。</summary>
        Text = 0,

        /// <summary>多行文本。</summary>
        LongText = 1,

        /// <summary>单选（选项来自 <see cref="ToolUiField.Options"/>）。</summary>
        Choice = 2,

        /// <summary>多选。</summary>
        MultiChoice = 3,

        /// <summary>布尔勾选。</summary>
        Bool = 4,

        /// <summary>只读的等宽文本块（如 diff、命令预览），不参与提交。</summary>
        Code = 5
    }

    /// <summary>
    /// 一个表单字段。字段名就是提交结果里的键名，因此必须是稳定的标识符。
    /// </summary>
    public sealed class ToolUiField
    {
        /// <summary>提交结果中的键名。</summary>
        public required string Name { get; init; }

        public required ToolUiFieldKind Kind { get; init; }

        /// <summary>显示标签；为空时直接显示 <see cref="Name"/>。</summary>
        public string? Label { get; init; }

        /// <summary><see cref="ToolUiFieldKind.Choice"/> / <see cref="ToolUiFieldKind.MultiChoice"/> 的候选项。</summary>
        public IReadOnlyList<string>? Options { get; init; }

        /// <summary>是否必填（提交时校验）。</summary>
        public bool Required { get; init; }

        /// <summary>默认值 / 预填值，以及 <see cref="ToolUiFieldKind.Code"/> 的只读内容。</summary>
        public string? Value { get; init; }

        /// <summary>占位提示。</summary>
        public string? Placeholder { get; init; }

        public static ToolUiField Text(string name, string? label = null, bool required = false, string? placeholder = null)
            => new() { Name = name, Kind = ToolUiFieldKind.Text, Label = label, Required = required, Placeholder = placeholder };

        public static ToolUiField LongText(string name, string? label = null, bool required = false, string? placeholder = null)
            => new() { Name = name, Kind = ToolUiFieldKind.LongText, Label = label, Required = required, Placeholder = placeholder };

        public static ToolUiField Choice(string name, IReadOnlyList<string> options, string? label = null, bool required = false)
            => new() { Name = name, Kind = ToolUiFieldKind.Choice, Options = options, Label = label, Required = required };

        public static ToolUiField MultiChoice(string name, IReadOnlyList<string> options, string? label = null)
            => new() { Name = name, Kind = ToolUiFieldKind.MultiChoice, Options = options, Label = label };

        public static ToolUiField Checkbox(string name, string? label = null)
            => new() { Name = name, Kind = ToolUiFieldKind.Bool, Label = label };

        public static ToolUiField CodeBlock(string content, string? label = null)
            => new() { Name = "code", Kind = ToolUiFieldKind.Code, Label = label, Value = content };
    }

    /// <summary>
    /// 工具提供的声明式表单。它是"通用工具 UI"的默认形态：
    /// 工具只描述数据，渲染与提交流程由宿主的通用表单渲染器负责，工具侧不需要任何 Blazor 代码。
    /// </summary>
    public sealed class ToolUiForm
    {
        public string? Title { get; init; }

        public string? Message { get; init; }

        public IReadOnlyList<ToolUiField> Fields { get; init; } = Array.Empty<ToolUiField>();

        /// <summary>
        /// "提问 + 选项"的常用形态：有选项时给出单选项，<paramref name="allowFreeText"/> 为真时
        /// 追加一个自由输入框；无选项时只给自由输入框。
        /// </summary>
        public static ToolUiForm Ask(string question, IReadOnlyList<string>? options, bool allowFreeText)
        {
            var fields = new List<ToolUiField>();

            if (options is { Count: > 0 })
            {
                // 单选必须选：否则"提交空答案"会被当成一个有效回答
                fields.Add(ToolUiField.Choice("choice", options, required: true));
                if (allowFreeText)
                    fields.Add(ToolUiField.Text("answer", placeholder: "…"));
            }
            else
            {
                fields.Add(ToolUiField.LongText("answer", required: true));
            }

            return new ToolUiForm
            {
                Title = question,
                Fields = fields
            };
        }
    }
}
