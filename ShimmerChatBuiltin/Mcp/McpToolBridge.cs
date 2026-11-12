using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// MCP 工具的 json schema → SharperLLM <see cref="Tool"/>.parameters 映射。
    /// SharperLLM 只支持扁平参数，因此对象/数组参数按 Object / Array 传递，
    /// 并把原始 schema 片段写入参数描述，供模型构造合法 JSON。
    /// </summary>
    public static class McpToolSchemaMapper
    {
        /// <summary>把 MCP 工具定义映射为 SharperLLM 工具定义。</summary>
        public static Tool ToTool(McpToolDescriptor descriptor, string exposedName)
        {
            var description = new StringBuilder(descriptor.Description ?? "");
            if (!string.IsNullOrWhiteSpace(descriptor.Title)
                && !string.Equals(descriptor.Title, descriptor.Description, StringComparison.Ordinal))
            {
                if (description.Length > 0) description.Append(' ');
                description.Append($"({descriptor.Title})");
            }

            var tool = new Tool
            {
                name = exposedName,
                description = description.ToString(),
                parameters = BuildParameters(descriptor.InputSchemaJson)
            };

            return tool;
        }

        /// <summary>解析 inputSchema，产出扁平参数列表。schema 缺失或非法时返回 null（等同于无参数）。</summary>
        public static List<(ToolParameter parameter, bool required)>? BuildParameters(string? inputSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(inputSchemaJson)) return null;

            JsonObject? schema;
            try
            {
                schema = JsonNode.Parse(inputSchemaJson) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }

            if (schema == null) return null;

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema["required"] is JsonArray requiredArray)
            {
                foreach (var item in requiredArray)
                {
                    if (item is JsonValue value) required.Add(value.ToString());
                }
            }

            var parameters = new List<(ToolParameter, bool)>();
            if (schema["properties"] is not JsonObject properties) return parameters.Count == 0 ? null : parameters;

            foreach (var (name, value) in properties)
            {
                if (value is not JsonObject property) continue;

                var parameter = new ToolParameter
                {
                    name = name,
                    type = MapType(property),
                    description = DescribeParameter(name, property)
                };

                if (property["enum"] is JsonArray enumValues)
                {
                    parameter.@enum = enumValues
                        .Select(v => v?.ToString() ?? "")
                        .Where(v => v.Length > 0)
                        .ToList();
                }

                parameters.Add((parameter, required.Contains(name)));
            }

            return parameters.Count == 0 ? null : parameters;
        }

        private static ParameterType MapType(JsonObject property)
        {
            var type = property["type"]?.ToString();

            if (string.IsNullOrEmpty(type) && property["anyOf"] is JsonArray anyOf)
            {
                // anyOf 常见于可空字段：取第一个非 null 的具体类型。
                type = anyOf
                    .OfType<JsonObject>()
                    .Select(o => o["type"]?.ToString())
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "null");
            }

            return type switch
            {
                "number" or "integer" => ParameterType.Number,
                "boolean" => ParameterType.Boolean,
                "array" => ParameterType.Array,
                "object" => ParameterType.Object,
                _ => ParameterType.String
            };
        }

        private static string DescribeParameter(string name, JsonObject property)
        {
            var sb = new StringBuilder();
            var description = property["description"]?.ToString();
            if (!string.IsNullOrWhiteSpace(description))
                sb.Append(description);
            else
                sb.Append(name);

            var type = property["type"]?.ToString();
            if (!string.IsNullOrEmpty(type)) sb.Append($" (type: {type})");

            if (property["enum"] is JsonArray enumValues)
            {
                var values = enumValues.Select(v => v?.ToString() ?? "").Where(v => v.Length > 0).ToList();
                if (values.Count > 0) sb.Append($" one of [{string.Join(", ", values)}]");
            }

            if (type is "object" or "array")
                sb.Append($"; JSON schema: {property.ToJsonString()}");

            return sb.ToString();
        }
    }

    /// <summary>MCP 工具的轻量描述（与 SDK 类型解耦，便于设置面板与节点复用）。</summary>
    public record McpToolDescriptor(
        string Name,
        string? Title,
        string? Description,
        string InputSchemaJson,
        bool? ReadOnlyHint,
        bool? DestructiveHint)
    {
        /// <summary>从 SDK 工具实例构建描述。</summary>
        public static McpToolDescriptor From(ModelContextProtocol.Client.McpClientTool tool)
        {
            var protocol = tool.ProtocolTool;
            return new McpToolDescriptor(
                protocol.Name,
                protocol.Title,
                protocol.Description,
                protocol.InputSchema.ValueKind == JsonValueKind.Undefined ? "" : protocol.InputSchema.GetRawText(),
                protocol.Annotations?.ReadOnlyHint,
                protocol.Annotations?.DestructiveHint);
        }
    }

    /// <summary>MCP 资源的轻量描述。</summary>
    public record McpResourceDescriptor(string Uri, string Name, string? Title, string? Description, string? MimeType, long? Size);

    /// <summary>MCP 提示词模板参数描述。</summary>
    public record McpPromptArgumentDescriptor(string Name, string? Title, string? Description, bool Required);

    /// <summary>MCP 提示词模板描述。</summary>
    public record McpPromptDescriptor(string Name, string? Title, string? Description, IReadOnlyList<McpPromptArgumentDescriptor> Arguments);

    /// <summary>
    /// 把 SDK 会话枚举到的工具/资源/提示词统一转换为与 SDK 解耦的轻量描述。
    /// 设置页探针与聊天面板共用，避免同一套转换逻辑在多处漂移。
    /// </summary>
    public static class McpDescriptorFactory
    {
        public static List<McpToolDescriptor> Tools(IReadOnlyList<ModelContextProtocol.Client.McpClientTool> tools) =>
            tools.Select(McpToolDescriptor.From).ToList();

        public static List<McpResourceDescriptor> Resources(IReadOnlyList<ModelContextProtocol.Client.McpClientResource> resources) =>
            resources.Select(r => new McpResourceDescriptor(r.Uri, r.Name, r.Title, r.Description, r.MimeType, null)).ToList();

        public static List<McpPromptDescriptor> Prompts(IReadOnlyList<ModelContextProtocol.Client.McpClientPrompt> prompts) =>
            prompts.Select(p => new McpPromptDescriptor(
                p.Name, p.Title, p.Description,
                p.ProtocolPrompt.Arguments?.Select(a => new McpPromptArgumentDescriptor(
                    a.Name, a.Title, a.Description, a.Required == true)).ToList()
                    ?? (IReadOnlyList<McpPromptArgumentDescriptor>)[]))
                .ToList();
    }

    /// <summary>
    /// 把单个 MCP 工具暴露为 ShimmerChat <see cref="IToolV3"/>。
    /// 工具名会被规整为 LLM 可安全调用的形式：mcp_{endpoint}_{tool}。
    /// </summary>
    public class McpToolBridge : IToolV3
    {
        private readonly McpEndpointSession _session;
        private readonly McpToolDescriptor _descriptor;
        private readonly string _exposedName;

        public McpToolBridge(McpEndpointSession session, McpToolDescriptor descriptor, string? exposedName = null)
        {
            _session = session;
            _descriptor = descriptor;
            _exposedName = exposedName ?? BuildExposedName(session.Config.Key, descriptor.Name);
        }

        /// <summary>暴露给 LLM 的工具名。</summary>
        public string ExposedName => _exposedName;

        /// <summary>服务端原始工具名。</summary>
        public string OriginalName => _descriptor.Name;

        public Tool GetDefinition() => McpToolSchemaMapper.ToTool(_descriptor, _exposedName);

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            try
            {
                var text = await _session.CallToolAsync(_descriptor.Name, context.ArgumentsJson, ct).ConfigureAwait(false);
                return ToolResult.Ok(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户/宿主取消：上抛给执行节点，绝不伪造成“工具失败”继续下一轮。
                throw;
            }
            catch (McpConfigException ex)
            {
                // 参数 JSON 非法属于预期内失败，给模型一个可直接读的错误文本。
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, ex.Message, ex.ToString());
            }
            catch (Exception ex)
            {
                // 协议/连接/服务端拒绝等：结构化失败；是否继续由 ToolCallLoop 的 continueOnToolError 决定。
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    $"MCP tool '{_descriptor.Name}' on endpoint '{_session.Config.Name}' failed: {ex.Message}",
                    ex.ToString());
            }
        }

        /// <summary>
        /// 生成 LLM 可安全调用的工具名：仅保留 [A-Za-z0-9_-]，总长不超过 64 字符
        /// （OpenAI 对 function.name 的限制），超长时保留前缀并用短哈希区分。
        /// </summary>
        public static string BuildExposedName(string endpointKey, string toolName)
        {
            var name = Sanitize(toolName);
            var prefix = $"mcp_{endpointKey}_";
            var maxNameLength = 64 - prefix.Length;

            if (maxNameLength <= 8)
            {
                // 端点 Key 过长：截断前缀，保证至少留下 8 个字符给工具名。
                var maxKeyLength = Math.Max(1, 64 - name.Length - 5);
                prefix = $"mcp_{endpointKey[..Math.Min(endpointKey.Length, maxKeyLength)]}_";
                maxNameLength = 64 - prefix.Length;
            }

            if (name.Length <= maxNameLength) return prefix + name;

            var hash = ShortHash(name);
            var keep = Math.Max(1, maxNameLength - hash.Length - 1);
            return prefix + name[..keep] + "_" + hash;
        }

        /// <summary>
        /// 计算暴露名并确保在 <paramref name="taken"/> 中唯一。
        /// sanitize 会把不同工具名（如 a/b 与 a_b）映射到同一暴露名，导致工具被静默丢弃；
        /// 此时用原始工具名的短哈希追加后缀，并在极端碰撞下继续加序号。
        /// </summary>
        public static string BuildUniqueExposedName(string endpointKey, string toolName, ISet<string> taken)
        {
            var baseName = BuildExposedName(endpointKey, toolName);
            if (!taken.Contains(baseName)) return baseName;

            var candidate = AppendSuffix(baseName, ShortHash(toolName));
            for (var i = 2; taken.Contains(candidate); i++)
                candidate = AppendSuffix(baseName, ShortHash(toolName) + "_" + i);

            return candidate;
        }

        private static string AppendSuffix(string name, string suffix)
        {
            var suffixWithSep = "_" + suffix;
            if (name.Length + suffixWithSep.Length <= 64)
                return name + suffixWithSep;
            return name[..(64 - suffixWithSep.Length)] + suffixWithSep;
        }

        private static string Sanitize(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');

            var result = sb.ToString().Trim('_');
            if (result.Length == 0) result = "tool";
            if (char.IsDigit(result[0])) result = "t" + result;
            return result;
        }

        private static string ShortHash(string value)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..6].ToLowerInvariant();
        }
    }

    /// <summary>
    /// 为端点注入一个可调用的资源工具：
    /// 传入 uri 读取资源内容；传入空参数时返回该端点可用资源的清单。
    /// </summary>
    public class McpResourceReadToolBridge : IToolV3
    {
        private readonly McpEndpointSession _session;
        private readonly string _exposedName;

        public McpResourceReadToolBridge(McpEndpointSession session)
        {
            _session = session;
            _exposedName = McpToolBridge.BuildExposedName(session.Config.Key, "read_mcp_resource");
        }

        public string ExposedName => _exposedName;

        public Tool GetDefinition() => new()
        {
            name = _exposedName,
            description =
                $"Read a resource from MCP endpoint '{_session.Config.Name}'. " +
                "Pass a resource URI to read it. Pass an empty object or {\"uri\":\"\"} to list the available resource URIs.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "uri",
                    type = ParameterType.String,
                    description = "Resource URI to read. Leave empty to list available resources."
                }, false)
            ]
        };

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            try
            {
                var uri = ExtractUri(context.ArgumentsJson);
                var text = string.IsNullOrWhiteSpace(uri)
                    ? await ListResourcesAsync(ct).ConfigureAwait(false)
                    : await _session.ReadResourceAsync(uri, ct).ConfigureAwait(false);
                return ToolResult.Ok(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户/宿主取消：上抛给执行节点，绝不伪造成“工具失败”。
                throw;
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    $"Failed to read resource from endpoint '{_session.Config.Name}': {ex.Message}",
                    ex.ToString());
            }
        }

        private async Task<string> ListResourcesAsync(CancellationToken ct)
        {
            var resources = await _session.GetResourcesAsync(ct).ConfigureAwait(false);
            if (resources.Count == 0)
                return $"Endpoint '{_session.Config.Name}' exposes no resources.";

            var sb = new StringBuilder();
            sb.AppendLine($"Resources available on endpoint '{_session.Config.Name}':");
            foreach (var resource in resources)
            {
                sb.Append("- ").Append(resource.Uri);
                if (!string.IsNullOrWhiteSpace(resource.Name)) sb.Append(" — ").Append(resource.Name);
                if (!string.IsNullOrWhiteSpace(resource.MimeType)) sb.Append(" [").Append(resource.MimeType).Append(']');
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string? ExtractUri(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            try
            {
                var node = JsonNode.Parse(input);
                if (node is JsonObject obj && obj["uri"] is JsonValue value)
                {
                    var text = value.ToString();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }
            catch (JsonException)
            {
                // 落回原样文本，模型偶尔会直接传 URI 字符串而非 JSON 对象。
            }

            var trimmed = input.Trim().Trim('"');
            return trimmed.Length == 0 || trimmed == "{}" ? null : trimmed;
        }
    }
}
