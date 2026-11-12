using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Fragments
{
    /// <summary>
    /// 高级片段节点：在文本之外还可注入图像内容块、tool_call_id、tool_calls、thinking
    /// 与消息级自定义参数，用于手工构造 system / tool_result / assistant 等复杂角色负载，
    /// 也用于在真实管线里复现「特殊角色携带图像」这类场景。
    /// <para>
    /// 这里只负责解析与构造，不判断某个角色“是否应该”携带某个字段：不同 OpenAI 兼容端点
    /// 以及客户端的 as_is 模式对角色负载的接受范围不同，约束应留给端点或下游节点。
    /// 唯一的结构性校验是 tool_result 必须带 tool_call_id（否则客户端会发出悬空工具结果）。
    /// </para>
    /// </summary>
    [NodeInfo("node.advanced_fragment", Icon = "▣", Color = "var(--node-fragment)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.advanced_fragment.desc")]
    public class AdvancedFragmentNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Advanced Fragment";

        /// <summary>片段角色，包含 tool_result 等复杂角色。</summary>
        [NodeProperty("prop.advanced_fragment.role", HintKey = "prop.advanced_fragment.role.hint", Order = 0)]
        public PromptBuilder.From From { get; set; } = PromptBuilder.From.user;

        /// <summary>文本内容；允许为空，以便构造纯图像消息。</summary>
        [NodeProperty("prop.advanced_fragment.content", HintKey = "prop.advanced_fragment.content.hint", MultiLine = true, Order = 1)]
        public string Content { get; set; } = "";

        /// <summary>
        /// 图像内容块数组（JSON）。元素可以是 base64 字符串、data URL，或
        /// { "mimeType": "image/png", "base64": "..." } / { "mimeType": "image/png", "fileName": "..." } 对象。
        /// 编辑器会以文件存储（fileName）或内联（base64）方式写入，取决于全局图像存储设置。
        /// </summary>
        [NodeProperty("prop.advanced_fragment.images_json", HintKey = "prop.advanced_fragment.images_json.hint",
            Editor = NodePropertyEditor.ImageList, Order = 2)]
        public string ImagesJson { get; set; } = "";

        /// <summary>tool_result 消息对应的 tool_call_id（写入 ChatMessage.id）。</summary>
        [NodeProperty("prop.advanced_fragment.tool_call_id", HintKey = "prop.advanced_fragment.tool_call_id.hint", Order = 3)]
        public string ToolCallId { get; set; } = "";

        /// <summary>
        /// assistant 消息的 tool_calls 数组（JSON），元素形如
        /// { "id": "call_1", "name": "tool", "arguments": { ... }, "index": 0 }。
        /// arguments 为对象时会序列化成字符串。
        /// </summary>
        [NodeProperty("prop.advanced_fragment.tool_calls_json", HintKey = "prop.advanced_fragment.tool_calls_json.hint", MultiLine = true, Order = 4)]
        public string ToolCallsJson { get; set; } = "";

        /// <summary>assistant 消息的 reasoning_content（thinking）。</summary>
        [NodeProperty("prop.advanced_fragment.thinking", HintKey = "prop.advanced_fragment.thinking.hint", MultiLine = true, Order = 5)]
        public string Thinking { get; set; } = "";

        /// <summary>消息级自定义参数（JSON 对象），会合并到请求体的 message 对象中。</summary>
        [NodeProperty("prop.advanced_fragment.custom_properties_json", HintKey = "prop.advanced_fragment.custom_properties_json.hint", MultiLine = true, Order = 6)]
        public string CustomPropertiesJson { get; set; } = "";

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            if (From == PromptBuilder.From.tool_result && string.IsNullOrWhiteSpace(ToolCallId))
            {
                return Fail(
                    NodeErrorCodes.ParameterError,
                    "AdvancedFragment: tool_result fragments require a tool_call_id.",
                    "Set ToolCallId to the id of the assistant tool call this result answers.");
            }

            var (images, imageFailure) = await ParseAndResolveImagesAsync(context);
            if (imageFailure != null)
                return imageFailure;

            var (toolCalls, toolCallFailure) = ParseToolCalls();
            if (toolCallFailure != null)
                return toolCallFailure;

            var (customProperties, customFailure) = ParseCustomProperties();
            if (customFailure != null)
                return customFailure;

            var message = new ChatMessage
            {
                Content = Content ?? string.Empty,
                // 空字符串表示不携带：客户端把非 null 的 thinking 一律回传成 reasoning_content
                thinking = string.IsNullOrEmpty(Thinking) ? null : Thinking,
                Images = images,
                toolCalls = toolCalls,
                CustomProperties = customProperties
            };

            if (!string.IsNullOrWhiteSpace(ToolCallId))
                message.id = ToolCallId;

            context.Env.Transient.Fragments.Add(new ContextSegment
            {
                SourceType = typeof(AdvancedFragmentNode),
                Message = message,
                From = From
            });

            return NodeResult.SuccessResult();
        }

        private NodeResult Fail(string code, string message, string? details)
            => NodeResult.Failure(code, message, details, Id, Name);

        /// <summary>
        /// 解析图像列表，并把文件引用解析为请求可用的 Base64 内容块。
        /// 图像块必须在进入客户端之前解析好：SharperLLM 只处理 Base64，不接触文件系统。
        /// </summary>
        private async Task<(List<ChatImage>? Images, NodeResult? Failure)> ParseAndResolveImagesAsync(
            PreNodeExecutionContext context)
        {
            var (parsed, failure) = ParseImages();
            if (failure != null)
                return (null, failure);

            if (parsed is not { Count: > 0 })
                return (null, null);

            var attachments = context.Env.Persistent.ImageAttachments;
            var resolved = new List<ChatImage>(parsed.Count);

            foreach (var image in parsed)
            {
                if (!string.IsNullOrEmpty(image.Base64))
                {
                    resolved.Add(ChatImage.FromBase64(image.Base64, image.MimeType));
                    continue;
                }

                if (attachments == null)
                {
                    return (null, Fail(
                        NodeErrorCodes.ServiceError,
                        "AdvancedFragment: image attachment service is unavailable; cannot resolve file-backed images.",
                        $"Image file '{image.FileName}' (mime '{image.MimeType}') requires PersistentEnv.ImageAttachments."));
                }

                try
                {
                    resolved.Add(await attachments.ToChatImageAsync(image, context.CancellationToken));
                }
                catch (Exception ex)
                {
                    return (null, Fail(
                        NodeErrorCodes.DataMissing,
                        $"AdvancedFragment: failed to load image '{image.FileName ?? image.MimeType}': {ex.Message}",
                        ex.ToString()));
                }
            }

            return (resolved, null);
        }

        private (List<MessageImage>? Images, NodeResult? Failure) ParseImages()
        {
            if (string.IsNullOrWhiteSpace(ImagesJson))
                return (null, null);

            JArray array;
            try
            {
                array = JArray.Parse(ImagesJson);
            }
            catch (JsonException ex)
            {
                return (null, Fail(
                    NodeErrorCodes.ParseError,
                    "AdvancedFragment: ImagesJson is not a valid JSON array.",
                    ex.ToString()));
            }

            var images = new List<MessageImage>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i];

                if (item.Type == JTokenType.String)
                {
                    var raw = item.Value<string>() ?? string.Empty;
                    if (ImageAttachmentRules.TryParseDataUrl(raw, out var dataUrlBase64, out var dataUrlMime))
                        images.Add(MessageImage.FromBase64(dataUrlBase64, dataUrlMime));
                    else
                        images.Add(MessageImage.FromBase64(raw, ChatImage.DefaultMimeType));

                    continue;
                }

                if (item is not JObject obj)
                {
                    return (null, Fail(
                        NodeErrorCodes.ParseError,
                        $"AdvancedFragment: image #{i} must be a base64 string, a data URL, or an object.",
                        $"Actual token type: {item.Type}."));
                }

                var mimeType = GetString(obj, "mimeType");
                if (string.IsNullOrWhiteSpace(mimeType))
                    mimeType = ChatImage.DefaultMimeType;

                var base64 = GetString(obj, "base64");
                var fileName = GetString(obj, "fileName");

                if (string.IsNullOrEmpty(base64) && string.IsNullOrEmpty(fileName))
                {
                    return (null, Fail(
                        NodeErrorCodes.DataMissing,
                        $"AdvancedFragment: image #{i} has neither base64 data nor a fileName.",
                        "Expected an object with a string 'base64' or 'fileName' property."));
                }

                images.Add(string.IsNullOrEmpty(base64)
                    ? MessageImage.FromFile(fileName!, mimeType)
                    : MessageImage.FromBase64(base64, mimeType));
            }

            return (images, null);
        }

        private static string? GetString(JObject obj, string property)
        {
            var token = obj.GetValue(property, StringComparison.OrdinalIgnoreCase);
            return token?.Type == JTokenType.String ? token.Value<string>() : null;
        }

        private (List<ToolCall>? ToolCalls, NodeResult? Failure) ParseToolCalls()
        {
            if (string.IsNullOrWhiteSpace(ToolCallsJson))
                return (null, null);

            JArray array;
            try
            {
                array = JArray.Parse(ToolCallsJson);
            }
            catch (JsonException ex)
            {
                return (null, Fail(
                    NodeErrorCodes.ParseError,
                    "AdvancedFragment: ToolCallsJson is not a valid JSON array.",
                    ex.ToString()));
            }

            var calls = new List<ToolCall>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JObject obj)
                {
                    return (null, Fail(
                        NodeErrorCodes.ParseError,
                        $"AdvancedFragment: tool call #{i} must be a JSON object.",
                        $"Actual token type: {array[i].Type}."));
                }

                var callId = obj["id"]?.ToString();
                var callName = obj["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(callName))
                {
                    return (null, Fail(
                        NodeErrorCodes.ParameterError,
                        $"AdvancedFragment: tool call #{i} requires both id and name.",
                        obj.ToString(Formatting.None)));
                }

                var argumentsToken = obj["arguments"];
                string? arguments = argumentsToken switch
                {
                    null => null,
                    { Type: JTokenType.String } => argumentsToken.Value<string>(),
                    _ => argumentsToken.ToString(Formatting.None)
                };

                var indexToken = obj["index"];
                var index = indexToken != null && indexToken.Type == JTokenType.Integer
                    ? indexToken.Value<int>()
                    : calls.Count;

                calls.Add(new ToolCall
                {
                    id = callId!,
                    name = callName!,
                    arguments = arguments,
                    index = index
                });
            }

            return (calls, null);
        }

        private (Dictionary<string, object>? Properties, NodeResult? Failure) ParseCustomProperties()
        {
            if (string.IsNullOrWhiteSpace(CustomPropertiesJson))
                return (null, null);

            JObject obj;
            try
            {
                obj = JObject.Parse(CustomPropertiesJson);
            }
            catch (JsonException ex)
            {
                return (null, Fail(
                    NodeErrorCodes.ParseError,
                    "AdvancedFragment: CustomPropertiesJson is not a valid JSON object.",
                    ex.ToString()));
            }

            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var property in obj.Properties())
                properties[property.Name] = property.Value;

            return (properties, null);
        }
    }
}
