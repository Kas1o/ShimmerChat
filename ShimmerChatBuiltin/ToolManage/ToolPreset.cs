namespace ShimmerChatBuiltin.ToolManage;

/// <summary>工具预设</summary>
public class ToolPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; } = false;
    public List<string> EnabledToolTypeNames { get; set; } = new();

    /// <summary>收藏标记；只影响预设选择器与检索列表的排序，不改变持久化顺序。</summary>
    public bool Pinned { get; set; }

    /// <summary>复制一份预设；Id 由调用方重新分配。</summary>
    public ToolPreset Clone() => new()
    {
        Id = "",
        Name = Name,
        IsDefault = false,
        EnabledToolTypeNames = new List<string>(EnabledToolTypeNames),
        Pinned = Pinned
    };
}