using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 单次工具调用的入参。只携带"每次调用独有"的信息——
    /// 会话级依赖（KVData / Chat / Agent 等）由工具通过 <see cref="IAutoCreateToolV2.Create(PersistentEnv)"/>
    /// 在构造期捕获，不在这里重复传递。
    /// </summary>
    public sealed class ToolInvocationContext
    {
        /// <summary>模型给出的 tool_call id（写回 tool_result 时用作 <c>ChatMessage.id</c>）。可能为空串。</summary>
        public required string ToolCallId { get; init; }

        /// <summary>工具名（= <c>GetDefinition().name</c>）。</summary>
        public required string ToolName { get; init; }

        /// <summary>模型给出的原始参数 JSON（通常是 "{}"）。</summary>
        public required string ArgumentsJson { get; init; }

        /// <summary>本轮序号（0-based）。</summary>
        public required int RoundIndex { get; init; }

        /// <summary>
        /// 把参数反序列化成类型化的 DTO。失败时返回 false 并给出可直接回给模型的错误文本，
        /// 调用方应把它变成 <see cref="ToolResult.Fail"/>，不要抛异常。
        /// </summary>
        public bool TryGetArguments<T>(out T? value, out string? error) where T : class
        {
            value = null;
            error = null;

            var json = string.IsNullOrWhiteSpace(ArgumentsJson) ? "{}" : ArgumentsJson;

            try
            {
                value = JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException ex)
            {
                error = $"Arguments are not valid JSON for tool '{ToolName}': {ex.Message}";
                return false;
            }

            if (value == null)
            {
                error = $"Arguments for tool '{ToolName}' are empty or null.";
                return false;
            }

            return true;
        }

        /// <summary>解析参数为可自由读取的 JSON 对象；非法 JSON 返回 null（不抛）。</summary>
        public JObject? TryGetArgumentsObject()
        {
            var json = string.IsNullOrWhiteSpace(ArgumentsJson) ? "{}" : ArgumentsJson;
            try
            {
                return JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
