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
