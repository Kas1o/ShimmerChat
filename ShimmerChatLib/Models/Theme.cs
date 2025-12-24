using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 主题模型
    /// </summary>
    public class Theme
    {
        /// <summary>
        /// 主题ID
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 主题名称
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// 主题描述
        /// </summary>
        [JsonProperty("description")]
        public string? Description { get; set; }

        /// <summary>
        /// 是否为默认主题
        /// </summary>
        [JsonProperty("isDefault")]
        public bool IsDefault { get; set; }

        /// <summary>
        /// 是否为内置主题（不可删除）
        /// </summary>
        [JsonProperty("isBuiltIn")]
        public bool IsBuiltIn { get; set; }

        // 颜色变量
        [JsonProperty("colorPrimary")]
        public string ColorPrimary { get; set; } = "#ffa200";

        [JsonProperty("colorPrimaryHover")]
        public string ColorPrimaryHover { get; set; } = "#ff7300";

        [JsonProperty("colorPrimaryActive")]
        public string ColorPrimaryActive { get; set; } = "#e66300";

        [JsonProperty("colorSecondary")]
        public string ColorSecondary { get; set; } = "#6b7280";

        [JsonProperty("colorSecondaryHover")]
        public string ColorSecondaryHover { get; set; } = "#565d6e";

        [JsonProperty("colorSecondaryActive")]
        public string ColorSecondaryActive { get; set; } = "#4b5563";

        [JsonProperty("colorSuccess")]
        public string ColorSuccess { get; set; } = "#10b981";

        [JsonProperty("colorSuccessHover")]
        public string ColorSuccessHover { get; set; } = "#0ea570";

        [JsonProperty("colorSuccessActive")]
        public string ColorSuccessActive { get; set; } = "#0d9264";

        [JsonProperty("colorWarning")]
        public string ColorWarning { get; set; } = "#f59e0b";

        [JsonProperty("colorWarningHover")]
        public string ColorWarningHover { get; set; } = "#d97706";

        [JsonProperty("colorWarningActive")]
        public string ColorWarningActive { get; set; } = "#b45309";

        [JsonProperty("colorDanger")]
        public string ColorDanger { get; set; } = "#ef4444";

        [JsonProperty("colorDangerHover")]
        public string ColorDangerHover { get; set; } = "#dc2626";

        [JsonProperty("colorDangerActive")]
        public string ColorDangerActive { get; set; } = "#b91c1c";

        [JsonProperty("colorInfo")]
        public string ColorInfo { get; set; } = "#3b82f6";

        [JsonProperty("colorInfoHover")]
        public string ColorInfoHover { get; set; } = "#2563eb";

        [JsonProperty("colorInfoActive")]
        public string ColorInfoActive { get; set; } = "#1d4ed8";

        [JsonProperty("colorTextPrimary")]
        public string ColorTextPrimary { get; set; } = "#111827";

        [JsonProperty("colorTextSecondary")]
        public string ColorTextSecondary { get; set; } = "#4b5563";

        [JsonProperty("colorTextTertiary")]
        public string ColorTextTertiary { get; set; } = "#9ca3af";

        [JsonProperty("colorTextInverse")]
        public string ColorTextInverse { get; set; } = "#ffffff";

        [JsonProperty("colorBgPrimary")]
        public string ColorBgPrimary { get; set; } = "#ffffff";

        [JsonProperty("colorBgSecondary")]
        public string ColorBgSecondary { get; set; } = "#f9fafb";

        [JsonProperty("colorBgTertiary")]
        public string ColorBgTertiary { get; set; } = "#f3f4f6";

        [JsonProperty("colorBgInverse")]
        public string ColorBgInverse { get; set; } = "#111827";

        [JsonProperty("colorBorderPrimary")]
        public string ColorBorderPrimary { get; set; } = "#e5e7eb";

        [JsonProperty("colorBorderSecondary")]
        public string ColorBorderSecondary { get; set; } = "#d1d5db";

        [JsonProperty("colorBorderTertiary")]
        public string ColorBorderTertiary { get; set; } = "#9ca3af";

        [JsonProperty("colorOverlay")]
        public string ColorOverlay { get; set; } = "rgba(0, 0, 0, 0.5)";

        [JsonProperty("shadowSm")]
        public string ShadowSm { get; set; } = "0 1px 2px 0 rgba(0, 0, 0, 0.05)";

        [JsonProperty("shadowBase")]
        public string ShadowBase { get; set; } = "0 1px 3px 0 rgba(0, 0, 0, 0.1), 0 1px 2px -1px rgba(0, 0, 0, 0.1)";

        [JsonProperty("shadowMd")]
        public string ShadowMd { get; set; } = "0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06)";

        [JsonProperty("shadowLg")]
        public string ShadowLg { get; set; } = "0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)";

        [JsonProperty("shadowXl")]
        public string ShadowXl { get; set; } = "0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)";

        [JsonProperty("shadow2Xl")]
        public string Shadow2Xl { get; set; } = "0 25px 50px -12px rgba(0, 0, 0, 0.25)";

        [JsonProperty("radiusXs")]
        public string RadiusXs { get; set; } = "0.125rem";

        [JsonProperty("radiusSm")]
        public string RadiusSm { get; set; } = "0.25rem";

        [JsonProperty("radiusMd")]
        public string RadiusMd { get; set; } = "0.375rem";

        [JsonProperty("radiusLg")]
        public string RadiusLg { get; set; } = "0.5rem";

        [JsonProperty("radiusXl")]
        public string RadiusXl { get; set; } = "0.75rem";

        [JsonProperty("radius2Xl")]
        public string Radius2Xl { get; set; } = "1rem";

        [JsonProperty("radius3Xl")]
        public string Radius3Xl { get; set; } = "1.5rem";

        [JsonProperty("radiusFull")]
        public string RadiusFull { get; set; } = "9999px";

        [JsonProperty("spacingXs")]
        public string SpacingXs { get; set; } = "0.25rem";

        [JsonProperty("spacingSm")]
        public string SpacingSm { get; set; } = "0.5rem";

        [JsonProperty("spacingMd")]
        public string SpacingMd { get; set; } = "0.75rem";

        [JsonProperty("spacingLg")]
        public string SpacingLg { get; set; } = "1rem";

        [JsonProperty("spacingXl")]
        public string SpacingXl { get; set; } = "1.5rem";

        [JsonProperty("spacing2Xl")]
        public string Spacing2Xl { get; set; } = "2rem";

        [JsonProperty("spacing3Xl")]
        public string Spacing3Xl { get; set; } = "2.5rem";

        [JsonProperty("borderSize")]
        public string BorderSize { get; set; } = "1px";

        [JsonProperty("isDarkMode")]
        public bool IsDarkMode { get; set; } = false;

        [JsonProperty("lightColorFactor")]
        public double LightColorFactor { get; set; } = 0.2;

        /// <summary>
        /// 自定义CSS
        /// </summary>
        [JsonProperty("customCss")]
        public string? CustomCss { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public Theme()
        {
            Id = Guid.NewGuid().ToString();
            Name = "新主题";
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public Theme Clone()
        {
            return (Theme)MemberwiseClone();
        }
    }
}
