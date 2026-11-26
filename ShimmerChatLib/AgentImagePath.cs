using ShimmerChatLib.Storage;

namespace ShimmerChatLib
{
    /// <summary>
    /// Agent 头像 / 背景图路径的唯一解析规则。
    /// <para>
    /// Agent 只保存<b>相对 UserUploadImage 目录</b>的路径（例如 <c>avatar.png</c> 或
    /// <c>sub/avatar.png</c>），不保存绝对路径，也不保存 Guid。这样数据目录可以整体迁移、
    /// 导出导入不受机器路径影响，同时仍由 Program.cs 的 <c>/userimages/</c> 静态资源统一提供访问。
    /// </para>
    /// <para>
    /// 磁盘路径与 URL 都从这里推导：调用方不得再自行拼接，也不得接受绝对路径或 <c>..</c>
    /// 之类的越界片段。非法路径一律返回 false / null（调用方按「无图」显式回退），
    /// 不会悄悄落到上传目录之外。
    /// </para>
    /// </summary>
    public static class AgentImagePath
    {
        /// <summary>UserUploadImage 目录对外暴露的静态资源请求路径。</summary>
        public const string RequestPath = "/userimages";

        /// <summary>UserUploadImage 目录的绝对路径。</summary>
        public static string RootDirectory => DataPaths.Current.UserUploadImageDirectory;

        /// <summary>
        /// 把持久化的相对路径规范化为 <c>/</c> 分隔的形式。
        /// <para>
        /// 空值 / 空白是合法状态（表示没有图像），此时返回 true 且 <paramref name="normalized"/>
        /// 为 null。绝对路径、盘符路径、空片段、<c>.</c> / <c>..</c>、包含非法文件名字符的片段
        /// 都返回 false，由调用方决定是回退为无图还是显式报错。
        /// </para>
        /// </summary>
        public static bool TryNormalize(string? path, out string? normalized)
        {
            normalized = null;

            if (string.IsNullOrWhiteSpace(path))
                return true;

            var trimmed = path.Trim();

            // Windows 的 "C:\x"、UNC 路径和 Unix 的 "/x" 都不是相对上传目录的路径。
            if (Path.IsPathRooted(trimmed))
                return false;

            var unified = trimmed.Replace('\\', '/');
            if (unified.StartsWith('/'))
                return false;

            var segments = unified.Split('/');
            foreach (var segment in segments)
            {
                if (!IsValidSegment(segment))
                    return false;
            }

            normalized = string.Join('/', segments);
            return true;
        }

        /// <summary>
        /// 把持久化路径转换为浏览器可直接使用的 <c>/userimages/...</c> 地址；
        /// 空路径或非法路径返回 null（界面回退到占位符，不渲染越界地址）。
        /// </summary>
        public static string? ToUrl(string? path)
        {
            if (!TryNormalize(path, out var normalized) || normalized == null)
                return null;

            var escaped = normalized
                .Split('/')
                .Select(Uri.EscapeDataString);

            return $"{RequestPath}/{string.Join('/', escaped)}";
        }

        /// <summary>
        /// 把持久化路径解析为磁盘绝对路径。空路径返回 true 且 <paramref name="fullPath"/> 为 null；
        /// 非法路径或解析结果越出 UserUploadImage 目录时返回 false。
        /// </summary>
        public static bool TryGetFullPath(string? path, out string? fullPath)
            => TryGetFullPath(RootDirectory, path, out fullPath);

        /// <summary>
        /// 指定根目录的重载，便于在不修改 <see cref="DataPaths.Current"/> 的情况下测试 / 复用。
        /// </summary>
        public static bool TryGetFullPath(string rootDirectory, string? path, out string? fullPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

            fullPath = null;

            if (!TryNormalize(path, out var normalized))
                return false;

            if (normalized == null)
                return true;

            var root = Path.GetFullPath(rootDirectory);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            string candidate;
            try
            {
                candidate = Path.GetFullPath(
                    Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // 路径本身无法落到文件系统：按非法路径处理，调用方回退 / 报错。
                return false;
            }

            // 规范化后仍必须位于根目录之内（防御 ".."、盘符切换等手写数据）。
            if (!candidate.StartsWith(rootPrefix, comparison))
                return false;

            fullPath = candidate;
            return true;
        }

        /// <summary>
        /// 解析上传 / 导入要写入的新文件路径。路径非法时显式抛异常，绝不写到不该写的位置。
        /// </summary>
        public static string GetFullPath(string relativePath)
        {
            if (!TryGetFullPath(relativePath, out var fullPath) || fullPath == null)
            {
                throw new InvalidOperationException(
                    $"Agent image path '{relativePath}' is invalid: it must be a relative path inside '{RootDirectory}'.");
            }

            return fullPath;
        }

        /// <summary>
        /// 生成新的图像文件名。文件仍以随机名落盘，路径字段只负责“指向哪里”，
        /// 不承载业务语义，因此不需要从 Agent 名称派生。
        /// </summary>
        public static string NewFileName(string extension = ".png")
        {
            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith('.'))
                throw new ArgumentException($"Image extension '{extension}' must start with '.'.", nameof(extension));

            foreach (var c in extension)
            {
                if (!IsValidSegmentChar(c))
                    throw new ArgumentException($"Image extension '{extension}' contains an invalid character '{c}'.", nameof(extension));
            }

            return $"{Guid.NewGuid():N}{extension}";
        }

        private static bool IsValidSegment(string segment)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return false;

            foreach (var c in segment)
            {
                if (!IsValidSegmentChar(c))
                    return false;
            }

            // Windows 会静默去掉结尾的空格和点，导致存进去的路径与实际文件对不上。
            return !segment.EndsWith(' ') && !segment.EndsWith('.');
        }

        private static bool IsValidSegmentChar(char c)
        {
            if (c < 32 || c == 127)
                return false;

            return c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|');
        }
    }
}
