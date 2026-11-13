using System.Text;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    /// <summary>
    /// 读取本地图像文件，并把图像本身随 tool_result 一起返回。
    /// <para>
    /// 文本工具（<see cref="FileSystemReadToolV2"/>）遇到二进制只会给出提示；模型如果要“看”图，
    /// 必须由工具把图像放进 <see cref="ToolResult.Images"/>，再由执行节点写到消息的
    /// <c>Message.Images</c>。只返回路径或链接不会让模型真正看到内容。
    /// </para>
    /// <para>
    /// 格式与大小上限复用 <see cref="ImageAttachmentRules"/>，与聊天上传、生成图像保持同一套约束；
    /// 格式按文件头识别而不是扩展名，避免把改了后缀的文本/二进制文件当成图像发给模型。
    /// </para>
    /// </summary>
    public sealed class FileSystemImageReadToolV2 : IAutoCreateToolV2, IToolV3
    {
        private readonly IKVDataService _kvData;

        public static string NameKey => "tool.read_image";
        public static string DescriptionKey => "tool.read_image.desc";
        public static string[] CategoryKeys => ["category.file_system", "category.read_write"];

        public FileSystemImageReadToolV2(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new FileSystemImageReadToolV2(env.KVData);

        public Tool GetDefinition() => new()
        {
            name = "read_image",
            description =
                "Read an image file from the local file system and attach it to this tool result so you can " +
                "actually see it. Supports PNG, JPEG, WebP and GIF up to " +
                $"{FileSystemHelper.FormatSize(ImageAttachmentRules.MaxBytes)}. " +
                "Use read_file for text files; it does not return image pixels.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "path",
                    type = ParameterType.String,
                    description = "The image file path to read. Can be absolute or relative."
                }, true)
            ]
        };

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            if (!context.TryGetArguments<ReadImageArguments>(out var args, out var argumentError) || args == null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, argumentError ?? "Invalid arguments.");

            var rawPath = args.path?.Trim();
            if (string.IsNullOrWhiteSpace(rawPath))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'path' is required.");

            string path;
            try
            {
                path = Path.GetFullPath(rawPath);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.InvalidArguments,
                    $"Invalid path: '{rawPath}'.",
                    ex.ToString());
            }

            var config = FileSystemConfigManager.Load(_kvData);
            if (!FileSystemConfigManager.IsPathAllowed(path, config))
                return ToolResult.Fail(
                    ToolErrorCodes.AccessDenied,
                    $"Access denied: path '{path}' is not allowed.");

            if (Directory.Exists(path))
                return ToolResult.Fail(
                    ToolErrorCodes.InvalidArguments,
                    $"Path is a directory, not an image file: {path}");

            if (!File.Exists(path))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, $"File not found: {path}");

            var file = new FileInfo(path);
            if (file.Length == 0)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, $"Image file is empty: {path}");

            if (file.Length > ImageAttachmentRules.MaxBytes)
                return ToolResult.Fail(
                    ToolErrorCodes.InvalidArguments,
                    $"Image file is too large: {path} is {FileSystemHelper.FormatSize(file.Length)}, " +
                    $"the limit is {FileSystemHelper.FormatSize(ImageAttachmentRules.MaxBytes)}.");

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户取消：交给执行节点按取消处理，不能伪装成工具失败。
                throw;
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ServiceError,
                    $"Failed to read image file: {path}",
                    ex.ToString());
            }

            var mimeType = DetectImageMimeType(bytes);
            if (mimeType == null)
                return ToolResult.Fail(
                    ToolErrorCodes.InvalidArguments,
                    $"Unsupported or unrecognized image format: {path}. " +
                    $"Supported formats: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)}.");

            var sb = new StringBuilder();
            sb.AppendLine($"Image: {path}");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine($"Size: {FileSystemHelper.FormatSize(file.Length)}");
            sb.AppendLine($"Last Modified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("The image is attached to this tool result so you can see it.");

            var image = MessageImage.FromBase64(Convert.ToBase64String(bytes), mimeType);
            return ToolResult.Ok(sb.ToString(), [image]);
        }

        /// <summary>
        /// 按文件头识别受支持的图像格式。返回 null 表示不是本工具支持的图像，
        /// 调用方必须显式报错而不是按扩展名猜测。
        /// </summary>
        internal static string? DetectImageMimeType(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "image/png";
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";

            if (bytes.Length >= 6
                && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
                && bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
                && bytes[5] == (byte)'a')
            {
                return "image/gif";
            }

            if (bytes.Length >= 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            {
                return "image/webp";
            }

            return null;
        }

        private sealed class ReadImageArguments
        {
            public string? path { get; set; }
        }
    }
}
