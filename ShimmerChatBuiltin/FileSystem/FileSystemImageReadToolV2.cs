using System.Text;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.FileSystem
{
    /// <summary>
    /// 读取本地图像文件，落盘后随 tool_result 一起返回「文件引用 + 稳定链接」。
    /// <para>
    /// 文本工具（<see cref="FileSystemReadToolV2"/>）遇到二进制只会给出提示；模型如果要“看”图，
    /// 必须由工具把图像放进 <see cref="ToolResult.Images"/>，再由执行节点写到消息的
    /// <c>Message.Images</c>。只返回路径或链接不会让模型真正看到内容。
    /// </para>
    /// <para>
    /// 图像不再内联 Base64（大图会把消息存储与每轮请求撑爆），而是落盘到 UserUploadImage、
    /// 消息里只保存文件引用，与生成图 / MCP 图像同一条路径。
    /// 落盘前按 <c>image_processing</c> 参数先处理：auto 跟随全局上传处理设置（默认）、
    /// original 原样、budget 用 <c>pixel_budget</c> 预算；原图始终留在用户磁盘上，
    /// 因此处理不损失任何用户数据。
    /// </para>
    /// <para>
    /// 格式与大小上限复用 <see cref="ImageAttachmentRules"/>，与聊天上传、生成图像保持同一套约束；
    /// 格式按文件头识别而不是扩展名，避免把改了后缀的文本/二进制文件当成图像发给模型。
    /// </para>
    /// </summary>
    public sealed class FileSystemImageReadToolV2 : IAutoCreateToolV2, IToolV3
    {
        private readonly IKVDataService _kvData;
        private readonly IImageAttachmentService? _imageAttachments;

        public static string NameKey => "tool.read_image";
        public static string DescriptionKey => "tool.read_image.desc";
        public static string[] CategoryKeys => ["category.file_system", "category.read_write"];

        public FileSystemImageReadToolV2(IKVDataService kvData, IImageAttachmentService? imageAttachments)
        {
            _kvData = kvData;
            _imageAttachments = imageAttachments;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new FileSystemImageReadToolV2(env.KVData, env.ImageAttachments);

        public Tool GetDefinition() => new()
        {
            name = "read_image",
            description =
                "Read an image file from the local file system and attach it to this tool result so you can " +
                "actually see it. Supports PNG, JPEG, WebP and GIF up to " +
                $"{FileSystemHelper.FormatSize(ImageAttachmentRules.MaxBytes)}. " +
                "The image is saved to the image folder and attached with a stable link. " +
                "Use read_file for text files; it does not return image pixels.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "path",
                    type = ParameterType.String,
                    description = "The image file path to read. Can be absolute or relative."
                }, true),
                (new ToolParameter
                {
                    name = "image_processing",
                    type = ParameterType.String,
                    description =
                        "How the attached image is processed before saving: 'auto' (default, follows the app's " +
                        "image processing settings), 'original' (keep the file as-is), or 'budget' (scale down " +
                        $"to the pixel budget given by 'pixel_budget', default {ImageAttachmentRules.DefaultPixelBudget}). " +
                        "Prefer 'budget' when reading large images you only need to understand roughly."
                }, false),
                (new ToolParameter
                {
                    name = "pixel_budget",
                    type = ParameterType.Number,
                    description =
                        "Pixel budget (max width x height) for 'budget' mode. Ignored otherwise."
                }, false)
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

            var mimeType = ImageMimeDetector.Detect(bytes);
            if (mimeType == null)
                return ToolResult.Fail(
                    ToolErrorCodes.InvalidArguments,
                    $"Unsupported or unrecognized image format: {path}. " +
                    $"Supported formats: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)}.");

            // 处理配置由工具参数决定：非法值显式失败，不猜测修正。
            var (processing, processingError) = ResolveProcessing(args);
            if (processingError != null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, processingError);

            if (_imageAttachments == null)
            {
                // 与 CodeAct 的图像输出一致：无图像存储的会话（如子代理虚拟环境）显式失败，
                // 不退回内联 Base64 掩盖问题。
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    "Image storage is unavailable in this session, so images cannot be attached.");
            }

            MessageImage image;
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                image = await _imageAttachments.SaveGeneratedAsync(stream, mimeType, processing, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.OutputWriteFailed,
                    $"Failed to save the image read from {path}: {ex.Message}",
                    ex.ToString());
            }

            var url = _imageAttachments.GetDisplayUrl(image);
            if (string.IsNullOrWhiteSpace(url))
            {
                // 落盘成功却拿不到链接属于存储契约被破坏，显式失败并指出引用，
                // 避免模型拿着一个不可见的图像继续对话。
                return ToolResult.Fail(
                    ToolErrorCodes.OutputWriteFailed,
                    "The image was saved but no display URL could be resolved.",
                    $"FileName={image.FileName}, MimeType={image.MimeType}");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Image: {path}");
            sb.AppendLine($"Type: {mimeType}");
            sb.AppendLine($"Size: {FileSystemHelper.FormatSize(file.Length)}");
            sb.AppendLine($"Last Modified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Saved as: {url}");
            sb.AppendLine("The image is attached to this tool result so you can see it.");

            return ToolResult.Ok(sb.ToString(), [image]);
        }

        /// <summary>
        /// 把工具参数折算成落盘处理配置。
        /// auto 跟随全局上传处理设置（原图模式即不处理）；budget 沿用全局的算法与质量，
        /// 只替换像素预算；original 一律不处理。
        /// </summary>
        private (ImageProcessingOptions? Processing, string? Error) ResolveProcessing(ReadImageArguments args)
        {
            var global = _imageAttachments?.UploadProcessing;
            var mode = (args.image_processing ?? "auto").Trim().ToLowerInvariant();

            switch (mode)
            {
                case "auto":
                    return (global, null);

                case "original":
                    return (null, null);

                case "budget":
                    return (new ImageProcessingOptions
                    {
                        PixelBudget = ImageAttachmentRules.ClampPixelBudget(
                            args.pixel_budget ?? ImageAttachmentRules.DefaultPixelBudget),
                        Algorithm = global?.Algorithm ?? ImageCompressionAlgorithm.WebP,
                        Quality = global?.Quality ?? ImageAttachmentRules.DefaultCompressionQuality
                    }, null);

                default:
                    return (null,
                        $"Unknown image_processing value '{args.image_processing}'. " +
                        "Use 'auto', 'original' or 'budget'.");
            }
        }

        private sealed class ReadImageArguments
        {
            public string? path { get; set; }

            public string? image_processing { get; set; }

            public long? pixel_budget { get; set; }
        }
    }
}
