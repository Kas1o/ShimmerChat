namespace ShimmerChatLib
{
    /// <summary>
    /// 图像字节 → MIME 类型识别与校验的唯一来源。
    /// <para>
    /// 文件系统读图、CodeAct 附加图像、MCP 返回图像都读这里，避免各处各抄一份文件头判断后悄悄漂移。
    /// 识别只看文件头，不信任扩展名或服务端声明——声明的类型只用于交叉校验。
    /// </para>
    /// </summary>
    public static class ImageMimeDetector
    {
        /// <summary>
        /// 按文件头识别受支持的图像格式（png/jpeg/gif/webp）。
        /// 返回 null 表示不是可用的图像，调用方必须显式报错或标注省略，不能按扩展名猜测。
        /// </summary>
        public static string? Detect(ReadOnlySpan<byte> bytes)
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "image/png";
            }

            // JPEG: FF D8 FF
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";

            // GIF: "GIF87a" / "GIF89a"
            if (bytes.Length >= 6
                && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
                && bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
                && bytes[5] == (byte)'a')
            {
                return "image/gif";
            }

            // WEBP: "RIFF" .... "WEBP"
            if (bytes.Length >= 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            {
                return "image/webp";
            }

            return null;
        }

        /// <summary>
        /// 校验「外部声明的 MIME + 实际字节」能否作为受支持的图像附件。
        /// 声明可省略（按字节自动识别）；声明了就必须与字节一致，不做静默修正。
        /// 失败时通过 <paramref name="error"/> 给出可直接展示的原因。
        /// </summary>
        public static bool TryResolve(
            string? declaredMimeType, ReadOnlySpan<byte> bytes, out string mimeType, out string? error)
        {
            mimeType = string.Empty;
            error = null;

            if (bytes.IsEmpty)
            {
                error = "image data is empty";
                return false;
            }

            var detected = Detect(bytes);
            if (detected == null)
            {
                error = "unrecognized image format (supported: " +
                        string.Join(", ", ImageAttachmentRules.SupportedMimeTypes) + ")";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(declaredMimeType))
            {
                var declared = declaredMimeType.Trim();

                if (!ImageAttachmentRules.IsSupportedMimeType(declared))
                {
                    error = $"unsupported image MIME type '{declared}' " +
                            $"(supported: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)})";
                    return false;
                }

                if (!string.Equals(declared, detected, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"declared image MIME type '{declared}' does not match the image data ('{detected}')";
                    return false;
                }
            }

            mimeType = detected;
            return true;
        }
    }
}
