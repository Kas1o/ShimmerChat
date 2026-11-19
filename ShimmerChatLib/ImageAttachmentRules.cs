using System.Text.RegularExpressions;
using SharperLLM.Util;

namespace ShimmerChatLib
{
    /// <summary>
    /// 图像附件的共享约束与解析规则。
    /// <para>
    /// 聊天上传、节点编辑器的图像列表和 <c>IImageAttachmentService</c> 必须对
    /// “支持哪些格式 / 单张多大 / 一次几张”给出一致答案，因此这里作为唯一来源；
    /// UI 的 accept 提示与上传校验都读它，避免两处各写一份列表后悄悄漂移。
    /// </para>
    /// </summary>
    public static class ImageAttachmentRules
    {
        /// <summary>MIME 类型 → 落盘扩展名。顺序也决定 <see cref="Accept"/> 的顺序。</summary>
        public static IReadOnlyDictionary<string, string> MimeToExtension { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
                ["image/gif"] = ".gif"
            };

        /// <summary>允许选择的图像 MIME 类型。</summary>
        public static IReadOnlyList<string> SupportedMimeTypes { get; } =
            MimeToExtension.Keys.ToList().AsReadOnly();

        /// <summary>file input 的 accept 属性。</summary>
        public static string Accept { get; } = string.Join(",", SupportedMimeTypes);

        /// <summary>单张图像的字节上限（SignalR 已放开到 50MiB，这里只做合理限制）。</summary>
        public const long MaxBytes = 20L * 1024 * 1024;

        /// <summary>单条消息 / 单个节点属性建议携带的最大图像数量。</summary>
        public const int MaxPerMessage = 5;

        // ── 处理模式 / 预算处理的参数边界（唯一来源，设置页与节点属性共用） ──

        /// <summary>像素预算默认值：2048 × 2048，对齐主流多模态模型的高清上限。</summary>
        public const long DefaultPixelBudget = 2048L * 2048;

        /// <summary>像素预算下限：再小将明显损害模型读图（文字、细节）。</summary>
        public const long MinPixelBudget = 512L * 512;

        /// <summary>像素预算上限：8192 × 8192，与生成图像的尺寸上限一致。</summary>
        public const long MaxPixelBudget = 8192L * 8192;

        /// <summary>有损编码的默认质量。</summary>
        public const int DefaultCompressionQuality = 85;

        /// <summary>有损编码质量下限。</summary>
        public const int MinCompressionQuality = 1;

        /// <summary>有损编码质量上限。</summary>
        public const int MaxCompressionQuality = 100;

        /// <summary>
        /// 把像素预算夹紧到合法区间。节点属性 / KV 设置里的值在消费前都应经过它，
        /// 使越界值（手改的 JSON、手输的数字）按显式边界收敛而不是流入解码器。
        /// </summary>
        public static long ClampPixelBudget(long budget)
            => Math.Clamp(budget, MinPixelBudget, MaxPixelBudget);

        /// <summary>把压缩质量夹紧到合法区间（语义同 <see cref="ClampPixelBudget"/>）。</summary>
        public static int ClampCompressionQuality(int quality)
            => Math.Clamp(quality, MinCompressionQuality, MaxCompressionQuality);

        private static readonly Regex DataUrlPattern = new(
            @"^data:(?<mime>[^;,]+)?;base64,(?<data>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public static bool IsSupportedMimeType(string? mimeType)
            => !string.IsNullOrWhiteSpace(mimeType)
               && SupportedMimeTypes.Contains(mimeType.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 解析 <c>data:{mime};base64,{data}</c> 形式；不是 data URL 时返回 false。
        /// MIME 缺省时使用 <see cref="ChatImage.DefaultMimeType"/>。
        /// </summary>
        public static bool TryParseDataUrl(string? value, out string base64, out string mimeType)
        {
            base64 = string.Empty;
            mimeType = ChatImage.DefaultMimeType;

            if (string.IsNullOrEmpty(value))
                return false;

            var match = DataUrlPattern.Match(value);
            if (!match.Success)
                return false;

            var mime = match.Groups["mime"].Value;
            if (!string.IsNullOrWhiteSpace(mime))
                mimeType = mime;

            base64 = match.Groups["data"].Value;
            return true;
        }
    }
}
