using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>MCP 返回、待附加到工具结果的一张图像（已通过格式与大小校验，尚未落盘）。</summary>
    public sealed record McpImageData(string MimeType, byte[] Data);

    /// <summary>MCP 内容的文本渲染结果，以及可随工具结果交给模型看的图像。</summary>
    public sealed record McpContentResult(string Text, IReadOnlyList<McpImageData> Images)
    {
        public static McpContentResult TextOnly(string text) => new(text, Array.Empty<McpImageData>());
    }

    /// <summary>
    /// 图像在文本渲染中的处理方式。
    /// </summary>
    public enum McpImageOutput
    {
        /// <summary>收集图像字节，由工具桥落盘并作为 tool_result 的消息图像附加。</summary>
        Attach,

        /// <summary>不收集字节，只在文本里标注「二进制内容未注入上下文」（资源注入、面板预览等只能放文本的场景）。</summary>
        DescribeOnly
    }

    /// <summary>
    /// 把 MCP 的 <see cref="CallToolResult"/> / <see cref="ReadResourceResult"/> 渲染为「文本 + 可附加图像」。
    /// <para>
    /// 渲染是纯函数式的（除日志外无副作用），图像是否真的进入消息由调用方决定：
    /// <see cref="McpImageOutput.Attach"/> 时把字节收集到 <see cref="McpContentResult.Images"/>，
    /// <see cref="McpImageOutput.DescribeOnly"/> 时只在文本中说明省略。
    /// </para>
    /// <para>
    /// 所有被丢弃的图像都必须留下可读原因（格式不支持、大小超限、声明类型与内容不符、数量超限），
    /// 不允许静默丢弃。
    /// </para>
    /// </summary>
    internal sealed class McpContentFormatter
    {
        private readonly string _endpointId;
        private readonly ILogger? _logger;

        public McpContentFormatter(string endpointId, ILogger? logger)
        {
            _endpointId = endpointId;
            _logger = logger;
        }

        /// <summary>
        /// 把 CallToolResult 渲染为文本：文本块原样拼接，图像块按 <paramref name="imageOutput"/> 附加或标注省略，
        /// 音频与非图像二进制只标注摘要；<c>structuredContent</c> 以 JSON 追加。
        /// </summary>
        public McpContentResult FormatToolResult(
            CallToolResult result, string toolName, McpImageOutput imageOutput = McpImageOutput.Attach)
        {
            var sb = new StringBuilder();
            var images = new List<McpImageData>();
            var source = $"tool '{toolName}'";

            if (result.IsError == true)
                sb.AppendLine($"[MCP tool '{toolName}' reported an error]");

            if (result.Content is { Count: > 0 })
            {
                foreach (var block in result.Content)
                    AppendContentBlock(sb, block, images, imageOutput, source);
            }

            if (result.StructuredContent is { } structured)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("structuredContent:");
                sb.AppendLine(structured.GetRawText());
            }

            if (sb.Length == 0)
                sb.Append($"[MCP tool '{toolName}' returned no content]");

            return new McpContentResult(sb.ToString().TrimEnd(), images);
        }

        /// <summary>
        /// 把 ReadResourceResult 渲染为文本：文本内容原样返回，图像资源按 <paramref name="imageOutput"/> 处理，
        /// 其它二进制内容标注类型与大小。
        /// </summary>
        public McpContentResult FormatResourceResult(
            ReadResourceResult result, string uri, McpImageOutput imageOutput = McpImageOutput.DescribeOnly)
        {
            var sb = new StringBuilder();
            var images = new List<McpImageData>();
            var source = $"resource '{uri}'";

            foreach (var contents in result.Contents)
            {
                switch (contents)
                {
                    case TextResourceContents text:
                        sb.AppendLine(text.Text);
                        break;
                    case BlobResourceContents blob:
                        AppendBlob(sb, blob.Uri, blob.MimeType, () => blob.DecodedData,
                            images, imageOutput, source, "binary resource");
                        break;
                    default:
                        sb.AppendLine($"[unsupported resource contents {contents.GetType().Name} for {contents.Uri}]");
                        break;
                }
            }

            if (sb.Length == 0)
                sb.Append($"[resource '{uri}' returned no content]");

            return new McpContentResult(sb.ToString().TrimEnd(), images);
        }

        private void AppendContentBlock(StringBuilder sb, ContentBlock block,
            List<McpImageData> images, McpImageOutput imageOutput, string source)
        {
            switch (block)
            {
                case TextContentBlock text:
                    sb.AppendLine(text.Text);
                    break;
                case EmbeddedResourceBlock embedded:
                    switch (embedded.Resource)
                    {
                        case TextResourceContents textResource:
                            sb.AppendLine(textResource.Text);
                            break;
                        case BlobResourceContents blob:
                            AppendBlob(sb, blob.Uri, blob.MimeType, () => blob.DecodedData,
                                images, imageOutput, source, "embedded binary resource");
                            break;
                        default:
                            sb.AppendLine("[embedded resource with unsupported contents omitted]");
                            break;
                    }
                    break;
                case ResourceLinkBlock link:
                    sb.AppendLine($"[resource link: {link.Uri}{(string.IsNullOrWhiteSpace(link.Name) ? "" : $" ({link.Name})")}]");
                    break;
                case ImageContentBlock image:
                    AppendImage(sb, "image", null, image.MimeType, () => image.DecodedData,
                        images, imageOutput, source);
                    break;
                case AudioContentBlock audio:
                    sb.AppendLine($"[audio content omitted ({audio.MimeType ?? "unknown type"}): " +
                                  "audio is not supported in chat messages]");
                    break;
                default:
                    sb.AppendLine($"[unsupported content block '{block.Type}' omitted]");
                    break;
            }
        }

        /// <summary>
        /// 二进制资源的处理：只有声明为图像（或未声明类型）才尝试按图像附加，
        /// 其它类型直接标注省略——不为一个未知大文件白白解码 base64。
        /// </summary>
        private void AppendBlob(StringBuilder sb, string uri, string? mimeType, Func<ReadOnlyMemory<byte>> readData,
            List<McpImageData> images, McpImageOutput imageOutput, string source, string label)
        {
            var isImageCandidate = string.IsNullOrWhiteSpace(mimeType)
                || mimeType.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase);

            if (isImageCandidate)
            {
                AppendImage(sb, label, uri, mimeType, readData, images, imageOutput, source);
                return;
            }

            ReadOnlyMemory<byte> data;
            try
            {
                data = readData();
            }
            catch (Exception ex)
            {
                // 边界层解码失败：保留一行可读标注，同时留完整日志，不让坏数据静默消失。
                _logger?.LogError(ex, "[MCP:{Endpoint}] {Source} {Label} data could not be decoded: {Message}",
                    _endpointId, source, label, ex.Message);
                sb.AppendLine($"[{label} {uri} ({mimeType}) omitted: binary data could not be decoded]");
                return;
            }

            sb.AppendLine($"[{label} {uri} ({mimeType}, {data.Length} bytes) omitted: " +
                          "binary content is not injected into context]");
        }

        private void AppendImage(StringBuilder sb, string label, string? uri, string? declaredMimeType,
            Func<ReadOnlyMemory<byte>> readData, List<McpImageData> images, McpImageOutput imageOutput, string source)
        {
            ReadOnlyMemory<byte> data;
            try
            {
                data = readData();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[MCP:{Endpoint}] {Source} {Label} data could not be decoded: {Message}",
                    _endpointId, source, label, ex.Message);
                sb.AppendLine($"[{label}{Where(uri)} content omitted: image data could not be decoded]");
                return;
            }

            var bytes = data.Span;
            var declared = string.IsNullOrWhiteSpace(declaredMimeType) ? "unknown type" : declaredMimeType.Trim();
            var where = Where(uri);

            if (bytes.IsEmpty)
            {
                WarnImage(source, $"{label} content omitted ({declared}): image data is empty");
                sb.AppendLine($"[{label}{where} content omitted ({declared}): image data is empty]");
                return;
            }

            if (bytes.Length > ImageAttachmentRules.MaxBytes)
            {
                WarnImage(source, $"{label} content omitted ({declared}, {bytes.Length} bytes): " +
                                  $"larger than the {ImageAttachmentRules.MaxBytes}-byte image limit");
                sb.AppendLine($"[{label}{where} content omitted ({declared}, {bytes.Length} bytes): " +
                              $"larger than the {ImageAttachmentRules.MaxBytes}-byte image limit]");
                return;
            }

            if (!ImageMimeDetector.TryResolve(declaredMimeType, bytes, out var mimeType, out var error))
            {
                WarnImage(source, $"{label} content omitted ({declared}, {bytes.Length} bytes): {error}");
                sb.AppendLine($"[{label}{where} content omitted ({declared}, {bytes.Length} bytes): {error}]");
                return;
            }

            if (imageOutput == McpImageOutput.DescribeOnly)
            {
                sb.AppendLine($"[{label}{where} {mimeType} ({bytes.Length} bytes) omitted: " +
                              "binary content is not injected into context]");
                return;
            }

            if (images.Count >= ImageAttachmentRules.MaxPerMessage)
            {
                WarnImage(source, $"{label} {mimeType} ({bytes.Length} bytes) omitted: " +
                                  $"at most {ImageAttachmentRules.MaxPerMessage} images can accompany one tool result");
                sb.AppendLine($"[{label}{where} {mimeType} ({bytes.Length} bytes) omitted: " +
                              $"at most {ImageAttachmentRules.MaxPerMessage} images can accompany one tool result]");
                return;
            }

            images.Add(new McpImageData(mimeType, data.ToArray()));
            sb.AppendLine($"[{label}{where} {mimeType} ({bytes.Length} bytes) attached to this tool result]");
        }

        private static string Where(string? uri) => string.IsNullOrWhiteSpace(uri) ? "" : $" {uri}";

        private void WarnImage(string source, string reason) =>
            _logger?.LogWarning("[MCP:{Endpoint}] {Source}: {Reason}", _endpointId, source, reason);
    }
}
