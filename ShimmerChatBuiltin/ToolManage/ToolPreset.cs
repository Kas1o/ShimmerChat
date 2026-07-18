namespace ShimmerChatBuiltin.ToolManage;

/// <summary>工具预设</summary>
public class ToolPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; } = false;
    public List<string> EnabledToolTypeNames { get; set; } = new();
}