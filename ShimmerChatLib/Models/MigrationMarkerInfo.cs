using System;

namespace ShimmerChatLib.Models
{
    /// <summary>
    /// 迁移标记信息
    /// </summary>
    public class MigrationMarkerInfo
    {
        /// <summary>
        /// 迁移时间
        /// </summary>
        public DateTime MigrationTime { get; set; }

        /// <summary>
        /// 源存储类型
        /// </summary>
        public string SourceStorage { get; set; } = string.Empty;

        /// <summary>
        /// 目标存储类型
        /// </summary>
        public string TargetStorage { get; set; } = string.Empty;

        /// <summary>
        /// 迁移的条目数量
        /// </summary>
        public int MigratedCount { get; set; }

        /// <summary>
        /// 是否清空了源数据
        /// </summary>
        public bool SourceCleared { get; set; }

        /// <summary>
        /// 迁移版本（用于未来兼容性）
        /// </summary>
        public int Version { get; set; } = 1;
    }
}
