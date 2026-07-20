using Newtonsoft.Json;

namespace ShimmerChatLib.Models
{
    public class Theme
    {
        // ── Identity ──────────────────────────────────────
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string? Description { get; set; }
        [JsonProperty("isDefault")]   public bool IsDefault { get; set; }
        [JsonProperty("isBuiltIn")]   public bool IsBuiltIn { get; set; }
        [JsonProperty("isDarkMode")]  public bool IsDarkMode { get; set; }

        // ── Surface 层级 (0=页面底, 1=卡片/面板, 2=弹窗/悬浮, 3=输入框/交替底) ──
        [JsonProperty("surface0")] public string Surface0 { get; set; } = "#f8f9fa";
        [JsonProperty("surface1")] public string Surface1 { get; set; } = "#ffffff";
        [JsonProperty("surface2")] public string Surface2 { get; set; } = "#ffffff";
        [JsonProperty("surface3")] public string Surface3 { get; set; } = "#f1f3f5";

        // ── Text 层级 (0=主, 1=次, 2=辅助/占位) ──
        [JsonProperty("text0")] public string Text0 { get; set; } = "#11181c";
        [JsonProperty("text1")] public string Text1 { get; set; } = "#4a5568";
        [JsonProperty("text2")] public string Text2 { get; set; } = "#8896a4";

        // ── Border 层级 ──
        [JsonProperty("border0")] public string Border0 { get; set; } = "#e2e8f0";
        [JsonProperty("border1")] public string Border1 { get; set; } = "#edf2f7";

        // ── Accent ──
        [JsonProperty("accent")]      public string Accent { get; set; }      = "#5e6ad2";
        [JsonProperty("accentHover")] public string AccentHover { get; set; } = "#4f5ac7";
        [JsonProperty("accentSoft")]  public string AccentSoft { get; set; }  = "#f0f1fd";

        // ── Semantic ──
        [JsonProperty("success")]     public string Success { get; set; }     = "#2da44e";
        [JsonProperty("successSoft")] public string SuccessSoft { get; set; } = "#e6f4ea";
        [JsonProperty("warning")]     public string Warning { get; set; }     = "#d97706";
        [JsonProperty("warningSoft")] public string WarningSoft { get; set; } = "#fef3c7";
        [JsonProperty("danger")]      public string Danger { get; set; }      = "#cf222e";
        [JsonProperty("dangerSoft")]  public string DangerSoft { get; set; }  = "#fde8e8";
        [JsonProperty("info")]        public string Info { get; set; }        = "#2563eb";
        [JsonProperty("infoSoft")]    public string InfoSoft { get; set; }    = "#eff6ff";

        // ── Node 语义色 ──
        [JsonProperty("nodeFlow")]     public string NodeFlow { get; set; }     = "#3b82f6";
        [JsonProperty("nodeBranch")]   public string NodeBranch { get; set; }   = "#f59e0b";
        [JsonProperty("nodeLink")]     public string NodeLink { get; set; }     = "#10b981";
        [JsonProperty("nodeFragment")] public string NodeFragment { get; set; } = "#6366f1";
        [JsonProperty("nodePrompt")]   public string NodePrompt { get; set; }   = "#a855f7";
        [JsonProperty("nodeTool")]     public string NodeTool { get; set; }     = "#22c55e";
        [JsonProperty("nodeMemory")]   public string NodeMemory { get; set; }   = "#eab308";
        [JsonProperty("nodeConfig")]   public string NodeConfig { get; set; }   = "#ef4444";
        [JsonProperty("nodeSubagent")] public string NodeSubagent { get; set; } = "#ec4899";
        [JsonProperty("nodeDebug")]    public string NodeDebug { get; set; }    = "#94a3b8";

        // ── Shadows ──
        [JsonProperty("shadowSm")] public string ShadowSm { get; set; } = "0 1px 2px rgba(0,0,0,0.04)";
        [JsonProperty("shadowMd")] public string ShadowMd { get; set; } = "0 4px 12px rgba(0,0,0,0.06)";
        [JsonProperty("shadowLg")] public string ShadowLg { get; set; } = "0 12px 32px rgba(0,0,0,0.10)";

        // ── Radii ──
        [JsonProperty("radiusSm")] public string RadiusSm { get; set; } = "4px";
        [JsonProperty("radiusMd")] public string RadiusMd { get; set; } = "6px";
        [JsonProperty("radiusLg")] public string RadiusLg { get; set; } = "10px";

        // ── Spacing (8级) ──
        [JsonProperty("space1")]  public string Space1 { get; set; }  = "4px";
        [JsonProperty("space2")]  public string Space2 { get; set; }  = "8px";
        [JsonProperty("space3")]  public string Space3 { get; set; }  = "12px";
        [JsonProperty("space4")]  public string Space4 { get; set; }  = "16px";
        [JsonProperty("space5")]  public string Space5 { get; set; }  = "20px";
        [JsonProperty("space6")]  public string Space6 { get; set; }  = "24px";
        [JsonProperty("space8")]  public string Space8 { get; set; }  = "32px";
        [JsonProperty("space10")] public string Space10 { get; set; } = "40px";

        // ── Typography ──
        [JsonProperty("fontSans")]  public string FontSans { get; set; }  = "-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Inter', Roboto, sans-serif";
        [JsonProperty("fontMono")]  public string FontMono { get; set; }  = "'SF Mono', 'Fira Code', 'Cascadia Code', Consolas, monospace";
        [JsonProperty("fontXs")]    public string FontXs { get; set; }    = "11px";
        [JsonProperty("fontSm")]    public string FontSm { get; set; }    = "12px";
        [JsonProperty("fontBase")]  public string FontBase { get; set; }  = "13px";
        [JsonProperty("fontMd")]    public string FontMd { get; set; }    = "14px";
        [JsonProperty("fontLg")]    public string FontLg { get; set; }    = "16px";

        // ── Misc ──
        [JsonProperty("borderSize")] public string BorderSize { get; set; } = "1px";
        [JsonProperty("transition")] public string Transition { get; set; } = "150ms ease";
        [JsonProperty("customCss")]  public string? CustomCss { get; set; }

        // ── Timestamps ──
        [JsonProperty("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonProperty("updatedAt")] public DateTime UpdatedAt { get; set; }

        public Theme()
        {
            Id = Guid.NewGuid().ToString();
            Name = "新主题";
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public Theme Clone() => (Theme)MemberwiseClone();
    }
}
