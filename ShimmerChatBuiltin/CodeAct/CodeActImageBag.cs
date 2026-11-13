using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.CodeAct
{
    /// <summary>
    /// CodeAct 的“返回图像容器”。只有脚本显式放进去的图像才会随工具结果回传，
    /// 被调用工具产生的图像不会自动出现。
    /// <para>
    /// 这样脚本可以自己决定：调用 <c>read_image</c> / <c>generate_image</c> 后先检查、
    /// 筛选，或干脆不返回图像；需要时再 <see cref="Add(ToolResult)"/> 放入容器。
    /// </para>
    /// <para>
    /// 所有失败都是显式的：图像输出关闭、超过数量上限、格式不受支持、图像既无数据也无文件
    /// 都会抛 <see cref="CodeActException"/>，不会静默丢弃。
    /// </para>
    /// </summary>
    public sealed class CodeActImageBag
    {
        private readonly bool _enabled;
        private readonly IImageAttachmentService? _attachments;
        private readonly List<MessageImage> _items = new();
        private readonly List<string> _urls = new();

        internal CodeActImageBag(bool enabled, IImageAttachmentService? attachments)
        {
            _enabled = enabled;
            _attachments = attachments;
        }

        /// <summary>当前容器里的图像数量。</summary>
        public int Count => _items.Count;

        /// <summary>容器中的图像快照。最终工具结果只会带上这些图像。</summary>
        public IReadOnlyList<MessageImage> Items => _items.ToList();

        /// <summary>可以解析出显示地址的图像 URL；内联图像为 data URL。</summary>
        public IReadOnlyList<string> Urls => _urls.ToList();

        /// <summary>把一个工具结果里的全部图像放入容器（工具失败时也允许取出其附带图像）。</summary>
        public void Add(ToolResult result)
        {
            if (result == null)
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "ToolResult is required.");

            if (result.Images is { Count: > 0 } images)
                Add(images);
        }

        /// <summary>放入一组图像（例如 <c>result.Images</c>）。</summary>
        public void Add(IEnumerable<MessageImage> images)
        {
            if (images == null)
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "Image collection is required.");

            var batch = images.ToList();
            if (batch.Count == 0)
                return;

            EnsureEnabled();
            EnsureCapacity(batch.Count);

            // 先验证并解析整批地址，再写入，避免一半成功一半失败的中间状态。
            var prepared = new List<(MessageImage Image, string? Url)>(batch.Count);
            foreach (var image in batch)
            {
                Validate(image);
                prepared.Add((image, ResolveUrl(image)));
            }

            foreach (var (image, url) in prepared)
                AddCore(image, url);
        }

        /// <summary>放入单张图像。</summary>
        public void Add(MessageImage image)
        {
            EnsureEnabled();
            EnsureCapacity(1);
            Validate(image);
            AddCore(image, ResolveUrl(image));
        }

        /// <summary>清空容器；已被调用工具返回、但脚本不想要的图像可以这样丢弃。</summary>
        public void Clear()
        {
            _items.Clear();
            _urls.Clear();
        }

        /// <summary>内部入口：图像已经由 <see cref="CodeActGlobals.AddImageAsync(string, string?)"/> 落盘。</summary>
        internal void AddSaved(MessageImage image, string url)
        {
            EnsureEnabled();
            EnsureCapacity(1);
            Validate(image);
            AddCore(image, string.IsNullOrWhiteSpace(url) ? ResolveUrl(image) : url);
        }

        private void AddCore(MessageImage image, string? url)
        {
            _items.Add(image);
            if (!string.IsNullOrWhiteSpace(url))
                _urls.Add(url);
        }

        private void EnsureEnabled()
        {
            if (!_enabled)
            {
                throw new CodeActException(
                    ToolErrorCodes.ImageOutputDisabled,
                    "Image output is disabled for this CodeAct node. Enable it in the node properties to return images.");
            }
        }

        private void EnsureCapacity(int incoming)
        {
            if (_items.Count + incoming > CodeActGlobals.MaxImages)
            {
                throw new CodeActException(
                    ToolErrorCodes.OutputWriteFailed,
                    $"Image limit reached: at most {CodeActGlobals.MaxImages} images can be returned with one CodeAct result " +
                    $"(already {_items.Count}, tried to add {incoming}).");
            }
        }

        private static void Validate(MessageImage? image)
        {
            if (image == null)
                throw new CodeActException(ToolErrorCodes.InvalidArguments, "Image is required.");

            if (!ImageAttachmentRules.IsSupportedMimeType(image.MimeType))
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Unsupported image MIME type: '{image.MimeType}'. " +
                    $"Supported formats: {string.Join(", ", ImageAttachmentRules.SupportedMimeTypes)}.");
            }

            if (string.IsNullOrWhiteSpace(image.Base64) && string.IsNullOrWhiteSpace(image.FileName))
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    "Image has neither inline Base64 data nor a stored file name.");
            }

            // 内联图像按 base64 长度估算，避免解码一份可能很大的数据只为了校验。
            if (!string.IsNullOrWhiteSpace(image.Base64)
                && image.Base64.Length > ImageAttachmentRules.MaxBytes * 4L / 3L)
            {
                throw new CodeActException(ToolErrorCodes.InvalidArguments,
                    $"Inline image is too large: {image.Base64.Length} base64 chars, " +
                    $"the limit is {ImageAttachmentRules.MaxBytes} bytes.");
            }
        }

        /// <summary>
        /// 内联图像返回 data URL；文件引用交给图像服务解析。
        /// 文件引用无法解析（服务缺失或文件丢失）时显式失败——坏图像不能在请求构建阶段才暴露。
        /// </summary>
        private string? ResolveUrl(MessageImage image)
        {
            if (!string.IsNullOrWhiteSpace(image.Base64))
                return $"data:{image.MimeType};base64,{image.Base64}";

            if (_attachments == null)
            {
                throw new CodeActException(ToolErrorCodes.ConfigInvalid,
                    $"Image '{image.FileName}' is stored as a file reference, " +
                    "but image storage is unavailable in the current session.");
            }

            var url = _attachments.GetDisplayUrl(image);
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new CodeActException(ToolErrorCodes.OutputWriteFailed,
                    $"Stored image '{image.FileName}' could not be resolved; the file may be missing.",
                    $"FileName={image.FileName}, MimeType={image.MimeType}");
            }

            return url;
        }
    }
}
