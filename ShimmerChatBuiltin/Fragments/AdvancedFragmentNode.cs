using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.API;
using SharperLLM.Util;
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
        private static readonly Regex DataUrlPattern = new(
            @"^data:(?<mime>[^;,]+)?;base64,(?<data>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

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
        /// { "mimeType": "image/png", "base64": "..." } 对象。
        /// </summary>
        [NodeProperty("prop.advanced_fragment.images_json", HintKey = "prop.advanced_fragment.images_json.hint", MultiLine = true, Order = 2)]
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

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            if (From == PromptBuilder.From.tool_result && string.IsNullOrWhiteSpace(ToolCallId))
            {
                return Task.FromResult(Fail(
                    NodeErrorCodes.ParameterError,
                    "AdvancedFragment: tool_result fragments require a tool_call_id.",
                    "Set ToolCallId to the id of the assistant tool call this result answers."));
            }

            var (images, imageFailure) = ParseImages();
            if (imageFailure != null)
                return Task.FromResult(imageFailure);

            var (toolCalls, toolCallFailure) = ParseToolCalls();
            if (toolCallFailure != null)
                return Task.FromResult(toolCallFailure);

            var (customProperties, customFailure) = ParseCustomProperties();
            if (customFailure != null)
                return Task.FromResult(customFailure);

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

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private NodeResult Fail(string code, string message, string? details)
            => NodeResult.Failure(code, message, details, Id, Name);

        private (List<ChatImage>? Images, NodeResult? Failure) ParseImages()
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

            var images = new List<ChatImage>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i];
                string? base64;
                string mimeType = ChatImage.DefaultMimeType;

                if (item.Type == JTokenType.String)
                {
                    var raw = item.Value<string>() ?? string.Empty;
                    var match = DataUrlPattern.Match(raw);
                    if (match.Success)
                    {
                        var mime = match.Groups["mime"].Value;
                        if (!string.IsNullOrWhiteSpace(mime))
                            mimeType = mime;

                        base64 = match.Groups["data"].Value;
                    }
                    else
                    {
                        base64 = raw;
                    }
                }
                else if (item is JObject obj)
                {
                    var base64Token = obj["base64"];
                    if (base64Token == null || base64Token.Type != JTokenType.String)
                    {
                        return (null, Fail(
                            NodeErrorCodes.DataMissing,
                            $"AdvancedFragment: image #{i} has no base64 data.",
                            "Expected an object with a string 'base64' property."));
                    }

                    base64 = base64Token.Value<string>();
                    var mimeToken = obj["mimeType"];
                    if (mimeToken != null
                        && mimeToken.Type == JTokenType.String
                        && !string.IsNullOrWhiteSpace(mimeToken.Value<string>()))
                    {
                        mimeType = mimeToken.Value<string>()!;
                    }
                }
                else
                {
                    return (null, Fail(
                        NodeErrorCodes.ParseError,
                        $"AdvancedFragment: image #{i} must be a base64 string, a data URL, or an object.",
                        $"Actual token type: {item.Type}."));
                }

                if (string.IsNullOrEmpty(base64))
                {
                    return (null, Fail(
                        NodeErrorCodes.DataMissing,
                        $"AdvancedFragment: image #{i} carries no base64 data.",
                        "An empty image block must not be sent to the API."));
                }

                images.Add(ChatImage.FromBase64(base64, mimeType));
            }

            return (images, null);
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
