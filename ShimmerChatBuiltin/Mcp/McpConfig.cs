using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// MCP 端点传输类型。
    /// </summary>
    public enum McpTransportType
    {
        /// <summary>本地子进程，通过 stdin/stdout 交换消息（对应 SDK StdioClientTransport）。</summary>
        Stdio = 0,

        /// <summary>
        /// HTTP。默认使用自动探测：先尝试 Streamable HTTP，失败后回退到 HTTP+SSE（2024-11-05）。
        /// 也可锁定为其中一种（对应 SDK HttpTransportMode）。
        /// </summary>
        Http = 1
    }

    /// <summary>HTTP 传输模式，与 SDK 的 HttpTransportMode 一一对应。</summary>
    public enum McpHttpMode
    {
        /// <summary>首选 Streamable HTTP，失败回退 SSE。推荐的兼容模式。</summary>
        AutoDetect = 0,

        /// <summary>仅使用 Streamable HTTP。</summary>
        StreamableHttp = 1,

        /// <summary>仅使用旧版 HTTP+SSE。</summary>
        Sse = 2
    }

    /// <summary>资源注入模式。节点级与端点级共用。</summary>
    public enum McpResourceInjection
    {
        /// <summary>不注入。</summary>
        Off = 0,

        /// <summary>使用端点配置中逐个资源的启用状态。</summary>
        Selected = 1,

        /// <summary>注入端点列出的全部资源（忽略逐项开关）。</summary>
        All = 2
    }

    /// <summary>
    /// 单个 MCP 端点的持久化配置。仅保存用户意图，不保存任何运行时连接状态。
    /// </summary>
    public class McpEndpointConfig
    {
        /// <summary>稳定标识，用于工具名前缀与配置引用。由名称派生，创建后不建议修改。</summary>
        public string Id { get; set; } = "";

        /// <summary>显示名称。</summary>
        public string Name { get; set; } = "";

        /// <summary>是否启用。禁用的端点在前生成节点与聊天面板中都会被整体跳过。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>传输类型。</summary>
        public McpTransportType Transport { get; set; } = McpTransportType.Stdio;

        // ---- STDIO ----

        /// <summary>可执行文件路径或命令名。</summary>
        public string Command { get; set; } = "";

        /// <summary>命令行参数（每行一个）。</summary>
        public List<string> Arguments { get; set; } = [];

        /// <summary>附加环境变量（每行 KEY=VALUE）。</summary>
        public List<string> Environment { get; set; } = [];

        /// <summary>是否继承宿主进程环境变量。</summary>
        public bool InheritEnvironment { get; set; } = true;

        /// <summary>进程工作目录，留空使用宿主目录。</summary>
        public string WorkingDirectory { get; set; } = "";

        /// <summary>握手超时（秒）。超时后连接失败并在面板/节点中报告。</summary>
        public int InitializationTimeoutSeconds { get; set; } = McpTimeouts.DefaultInitializationSeconds;

        // ---- HTTP ----

        /// <summary>Streamable HTTP / SSE 端点 URL。</summary>
        public string Url { get; set; } = "";

        /// <summary>HTTP 传输模式。</summary>
        public McpHttpMode HttpMode { get; set; } = McpHttpMode.AutoDetect;

        /// <summary>附加 HTTP 请求头（每行 KEY: VALUE）。常用于 Authorization。</summary>
        public List<string> Headers { get; set; } = [];

        // ---- 暴露策略 ----

        /// <summary>是否把该端点的工具列表加入前生成上下文的可用工具。</summary>
        public bool ExposeTools { get; set; } = true;

        /// <summary>未在 <see cref="Tools"/> 中显式列出的工具默认是否启用。</summary>
        public bool ToolsEnabledByDefault { get; set; } = true;

        /// <summary>工具级开关。为空表示按 <see cref="ToolsEnabledByDefault"/> 全量处理。</summary>
        public List<McpNamedToggle> Tools { get; set; } = [];

        /// <summary>资源注入模式（端点级默认值，节点可覆盖）。</summary>
        public McpResourceInjection ResourceInjection { get; set; } = McpResourceInjection.Off;

        /// <summary>资源级开关。仅在 <see cref="ResourceInjection"/> 为 Selected 时生效。为空时按全量处理。</summary>
        public List<McpNamedToggle> Resources { get; set; } = [];

        /// <summary>是否为该端点额外注入一个可调用 MCP 资源的工具（resources/read）。</summary>
        public bool ExposeResourceReadTool { get; set; }

        /// <summary>资源上下文注入的总字符预算，超出部分截断并标注。</summary>
        public int ResourceCharBudget { get; set; } = 24000;

        /// <summary>
        /// 生成工具名/资源名使用的短标识。Id 已被约束为小写字母数字与 '_'。
        /// </summary>
        public string Key => string.IsNullOrWhiteSpace(Id) ? "mcp" : Id;

        /// <summary>
        /// 计算该端点应暴露给 LLM 的工具名列表：
        /// 显式开启的条目始终包含，未列出的条目由 <see cref="ToolsEnabledByDefault"/> 决定。
        /// </summary>
        public IEnumerable<string> ResolveEnabledTools(IEnumerable<string> available) =>
            ResolveToggles(available, Tools, ToolsEnabledByDefault);

        /// <summary>
        /// 计算该端点应注入上下文的资源：显式开启的条目始终包含，
        /// 未列出的条目由 <see cref="ResourceInjection"/> 决定。
        /// </summary>
        public IEnumerable<string> ResolveEnabledResources(IEnumerable<string> available) =>
            ResolveToggles(available, Resources, ResourceInjection == McpResourceInjection.All);

        /// <summary>工具/资源共用的开关解析逻辑。</summary>
        public static IEnumerable<string> ResolveToggles(
            IEnumerable<string> available, List<McpNamedToggle> toggles, bool enabledByDefault)
        {
            var explicitOn = new HashSet<string>(
                toggles.Where(t => t.Enabled).Select(t => t.Name), StringComparer.Ordinal);
            var known = new HashSet<string>(toggles.Select(t => t.Name), StringComparer.Ordinal);

            foreach (var name in available)
            {
                if (explicitOn.Contains(name)) yield return name;
                else if (enabledByDefault && !known.Contains(name)) yield return name;
            }
        }

        /// <summary>
        /// 取指定资源的生效开关状态（用于设置面板渲染）。
        /// 未显式配置时回落到 <see cref="ResourceInjection"/> / <see cref="ToolsEnabledByDefault"/>。
        /// </summary>
        public bool IsToolEnabled(string name)
        {
            var toggle = Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            return toggle?.Enabled ?? ToolsEnabledByDefault;
        }

        /// <summary>取指定资源的生效开关状态（用于设置面板渲染）。</summary>
        public bool IsResourceEnabled(string uri)
        {
            var toggle = Resources.FirstOrDefault(r => string.Equals(r.Name, uri, StringComparison.Ordinal));
            return toggle?.Enabled ?? ResourceInjection == McpResourceInjection.All;
        }

        /// <summary>设置工具开关状态，不存在时创建条目。</summary>
        public void SetToolEnabled(string name, bool enabled)
        {
            var toggle = Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (toggle == null)
            {
                toggle = new McpNamedToggle { Name = name };
                Tools.Add(toggle);
            }
            toggle.Enabled = enabled;
        }

        /// <summary>设置资源开关状态，不存在时创建条目。</summary>
        public void SetResourceEnabled(string uri, bool enabled)
        {
            var toggle = Resources.FirstOrDefault(r => string.Equals(r.Name, uri, StringComparison.Ordinal));
            if (toggle == null)
            {
                toggle = new McpNamedToggle { Name = uri };
                Resources.Add(toggle);
            }
            toggle.Enabled = enabled;
        }
    }

    /// <summary>具名开关，用于工具/资源级别的启用状态。</summary>
    public class McpNamedToggle
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    /// <summary>协议超时常量（秒）。</summary>
    public static class McpTimeouts
    {
        public const int DefaultInitializationSeconds = 30;
        public const int ToolCallSeconds = 180;
        public const int HttpRequestSeconds = 60;
    }

    /// <summary>
    /// MCP 端点目录：以 KVData 为唯一真源，读写均带明确的失败语义（不静默降级）。
    /// </summary>
    public static class McpEndpointCatalog
    {
        public const string SpaceId = "Mcp";
        public const string Key = "servers";

        /// <summary>读取全部端点。JSON 损坏时抛出 <see cref="McpConfigException"/>，由调用方决定如何呈现。</summary>
        public static List<McpEndpointConfig> Load(IKVDataService kvData)
        {
            var json = kvData.Read(SpaceId, Key);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<McpEndpointConfig>>(json)
                    ?? throw new McpConfigException($"MCP endpoint catalog deserialized to null (space '{SpaceId}', key '{Key}').");
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw new McpConfigException($"MCP endpoint catalog is not valid JSON: {ex.Message}", ex);
            }
        }

        public static void Save(IKVDataService kvData, List<McpEndpointConfig> endpoints)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(endpoints, Newtonsoft.Json.Formatting.Indented);
            kvData.Write(SpaceId, Key, json);
        }

        /// <summary>按 Id 精确查找端点（大小写不敏感）。</summary>
        public static McpEndpointConfig? Find(IEnumerable<McpEndpointConfig> endpoints, string id) =>
            endpoints.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 从显示名称派生唯一 Id（小写字母数字与下划线）。名称重复时追加 _2、_3 等后缀。
        /// </summary>
        public static string CreateUniqueId(string displayName, IEnumerable<McpEndpointConfig> existing)
        {
            var baseId = NormalizeId(displayName);
            var taken = new HashSet<string>(existing.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

            if (!taken.Contains(baseId)) return baseId;

            for (int i = 2; i < 10000; i++)
            {
                var candidate = $"{baseId}_{i}";
                if (!taken.Contains(candidate)) return candidate;
            }

            throw new McpConfigException($"Unable to allocate a unique MCP endpoint id for '{displayName}'.");
        }

        /// <summary>把任意显示名映射为标识符安全的字符串。</summary>
        public static string NormalizeId(string displayName)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in displayName.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch is ' ' or '-' or '_' or '.' or '/') sb.Append('_');
            }

            var result = sb.ToString().Trim('_');
            while (result.Contains("__")) result = result.Replace("__", "_");
            if (result.Length == 0) result = "server";
            if (char.IsDigit(result[0])) result = "s_" + result;
            return result.Length > 32 ? result[..32].Trim('_') : result;
        }

        /// <summary>
        /// 校验端点配置，返回用户可读的错误列表。空列表表示通过。
        /// 仅做本地可判定性检查，不进行任何网络/进程操作。
        /// </summary>
        public static List<string> Validate(McpEndpointConfig endpoint, ILocService loc)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(endpoint.Name))
                errors.Add(loc["mcp_err.name_required"]);

            if (string.IsNullOrWhiteSpace(endpoint.Id))
                errors.Add(loc["mcp_err.id_required"]);
            else if (endpoint.Id.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                errors.Add(loc["mcp_err.id_charset"]);

            switch (endpoint.Transport)
            {
                case McpTransportType.Stdio:
                    if (string.IsNullOrWhiteSpace(endpoint.Command))
                        errors.Add(loc["mcp_err.command_required"]);
                    if (endpoint.InitializationTimeoutSeconds <= 0)
                        errors.Add(loc["mcp_err.timeout_positive"]);
                    foreach (var line in endpoint.Environment)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.IndexOf('=') <= 0) errors.Add(loc.Format("mcp_err.env_format", line));
                    }
                    break;

                case McpTransportType.Http:
                    if (string.IsNullOrWhiteSpace(endpoint.Url))
                        errors.Add(loc["mcp_err.url_required"]);
                    else if (!Uri.TryCreate(endpoint.Url.Trim(), UriKind.Absolute, out var uri)
                             || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        errors.Add(loc.Format("mcp_err.url_invalid", endpoint.Url));
                    foreach (var line in endpoint.Headers)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.IndexOf(':') <= 0) errors.Add(loc.Format("mcp_err.header_format", line));
                    }
                    break;

                default:
                    errors.Add(loc.Format("mcp_err.transport_unknown", endpoint.Transport));
                    break;
            }

            if (endpoint.ResourceCharBudget < 0)
                errors.Add(loc["mcp_err.budget_negative"]);

            return errors;
        }

        /// <summary>把 "KEY=VALUE" 行解析为环境变量字典。</summary>
        public static Dictionary<string, string> ParseEnvironment(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                result[line[..idx].Trim()] = line[(idx + 1)..];
            }
            return result;
        }

        /// <summary>把 "KEY: VALUE" 行解析为请求头字典。</summary>
        public static Dictionary<string, string> ParseHeaders(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                result[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
            return result;
        }

        /// <summary>深拷贝，避免面板编辑直接改动已加载实例。</summary>
        public static McpEndpointConfig Clone(McpEndpointConfig source) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<McpEndpointConfig>(
                Newtonsoft.Json.JsonConvert.SerializeObject(source))!;

        /// <summary>创建带默认值的新端点。</summary>
        public static McpEndpointConfig CreateNew(string displayName, IEnumerable<McpEndpointConfig> existing) => new()
        {
            Id = CreateUniqueId(displayName, existing),
            Name = displayName
        };
    }

    /// <summary>MCP 配置层的可报告错误。调用方负责转换为 NodeResult 或面板提示。</summary>
    public class McpConfigException : Exception
    {
        public McpConfigException(string message) : base(message) { }
        public McpConfigException(string message, Exception inner) : base(message, inner) { }
    }
}
