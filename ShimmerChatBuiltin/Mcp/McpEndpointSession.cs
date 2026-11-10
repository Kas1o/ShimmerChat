using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>
    /// 单个 MCP 端点的会话包装。内部使用官方 C# SDK（ModelContextProtocol.Core）：
    /// STDIO 走 <see cref="StdioClientTransport"/>，HTTP 走 <see cref="HttpClientTransport"/>，
    /// 且默认 HttpTransportMode.AutoDetect —— 先尝试 Streamable HTTP，失败回退 HTTP+SSE。
    /// </summary>
    public sealed class McpEndpointSession : IAsyncDisposable
    {
        private readonly McpClient _client;
        private readonly ILoggerFactory? _loggerFactory;

        private IReadOnlyList<McpClientTool>? _tools;
        private IReadOnlyList<McpClientResource>? _resources;
        private IReadOnlyList<McpClientPrompt>? _prompts;

        private McpEndpointSession(McpEndpointConfig config, McpClient client, ILoggerFactory? loggerFactory)
        {
            Config = config;
            _client = client;
            _loggerFactory = loggerFactory;
        }

        public McpEndpointConfig Config { get; }

        public McpClient Client => _client;

        /// <summary>服务端自报信息（name/version），未握手时为 null。</summary>
        public Implementation? ServerInfo => _client.ServerInfo;

        /// <summary>服务端自报的使用说明，可为 null。</summary>
        public string? ServerInstructions => _client.ServerInstructions;

        public ServerCapabilities? ServerCapabilities => _client.ServerCapabilities;

        private ILogger? Logger => _loggerFactory?.CreateLogger("ShimmerChat.Mcp");

        /// <summary>
        /// 建立连接并完成 initialize 握手。
        /// 失败时抛出 <see cref="McpConnectException"/>，其中包含面向用户的端点标识与原始异常。
        /// </summary>
        public static async Task<McpEndpointSession> ConnectAsync(
            McpEndpointConfig config, ILoggerFactory? loggerFactory, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(config.Id) && config.Id.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                throw new McpConfigException($"MCP endpoint id '{config.Id}' contains characters other than [A-Za-z0-9_].");

            IClientTransport candidate;
            var options = new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "ShimmerChat", Version = McpClientInfo.Version },
                InitializationTimeout = TimeSpan.FromSeconds(Math.Max(1, config.InitializationTimeoutSeconds))
            };

            switch (config.Transport)
            {
                case McpTransportType.Stdio:
                    {
                        var stdioOptions = new StdioClientTransportOptions
                        {
                            Name = $"mcp:{config.Id}",
                            Command = config.Command,
                            Arguments = config.Arguments.ToList(),
                            WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
                            InheritEnvironmentVariables = config.InheritEnvironment,
                            EnvironmentVariables = McpEndpointCatalog.ParseEnvironment(config.Environment)
                        };

                        // 服务端 stderr 是日志通道：写入调试输出，绝不当成协议消息。
                        var logger = loggerFactory?.CreateLogger("ShimmerChat.Mcp.Stdio");
                        stdioOptions.StandardErrorLines = line =>
                            logger?.LogDebug("[MCP:{Endpoint}] {Line}", config.Id, line);

                        candidate = new StdioClientTransport(stdioOptions, loggerFactory);
                        break;
                    }

                case McpTransportType.Http:
                    {
                        var httpOptions = new HttpClientTransportOptions
                        {
                            Name = $"mcp:{config.Id}",
                            Endpoint = new Uri(config.Url.Trim(), UriKind.Absolute),
                            TransportMode = config.HttpMode switch
                            {
                                McpHttpMode.StreamableHttp => HttpTransportMode.StreamableHttp,
                                McpHttpMode.Sse => HttpTransportMode.Sse,
                                _ => HttpTransportMode.AutoDetect
                            },
                            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, config.InitializationTimeoutSeconds)),
                            AdditionalHeaders = McpEndpointCatalog.ParseHeaders(config.Headers)
                        };

                        candidate = new HttpClientTransport(httpOptions, loggerFactory);
                        break;
                    }

                default:
                    throw new McpConfigException($"Unknown MCP transport type '{config.Transport}' for endpoint '{config.Id}'.");
            }

            try
            {
                // McpClient.CreateAsync 内部会调用 transport.ConnectAsync()；
                // 失败时由我们负责释放传输（IClientTransport 本身是 IAsyncDisposable）。
                var client = await McpClient.CreateAsync(candidate, options, loggerFactory, ct).ConfigureAwait(false);
                return new McpEndpointSession(config, client, loggerFactory);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await SafeDisposeAsync(candidate, loggerFactory).ConfigureAwait(false);
                throw;
            }
            catch (McpConnectException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await SafeDisposeAsync(candidate, loggerFactory).ConfigureAwait(false);
                throw new McpConnectException(
                    $"Failed to connect to MCP endpoint '{config.Name}' ({config.Id}, {Describe(config)}): {ex.Message}", ex);
            }
        }

        /// <summary>面向面板/错误的传输描述。</summary>
        public static string Describe(McpEndpointConfig config) => config.Transport switch
        {
            McpTransportType.Stdio => $"stdio: {config.Command} {string.Join(' ', config.Arguments)}".TrimEnd(),
            McpTransportType.Http => $"http[{config.HttpMode}]: {config.Url}",
            _ => config.Transport.ToString()
        };

        /// <summary>列出工具（带进程内缓存）。</summary>
        public async Task<IReadOnlyList<McpClientTool>> GetToolsAsync(CancellationToken ct)
        {
            return _tools ??= (await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false)).ToList();
        }

        /// <summary>
        /// 列出资源。部分服务端未实现 resources/list（或未声明 resources 能力）时返回空列表，
        /// 其余错误照常上抛。
        /// </summary>
        public async Task<IReadOnlyList<McpClientResource>> GetResourcesAsync(CancellationToken ct)
        {
            if (_resources != null) return _resources;

            try
            {
                return _resources = (await _client.ListResourcesAsync(cancellationToken: ct).ConfigureAwait(false)).ToList();
            }
            catch (McpProtocolException ex) when (IsUnsupportedMethod(ex))
            {
                Logger?.LogDebug("[MCP:{Endpoint}] resources/list unsupported: {Message}", Config.Id, ex.Message);
                return _resources = [];
            }
        }

        /// <summary>
        /// 列出提示词模板。部分服务端未实现 prompts/list（或未声明 prompts 能力）时返回空列表，
        /// 其余错误照常上抛。
        /// </summary>
        public async Task<IReadOnlyList<McpClientPrompt>> GetPromptsAsync(CancellationToken ct)
        {
            if (_prompts != null) return _prompts;

            try
            {
                return _prompts = (await _client.ListPromptsAsync(cancellationToken: ct).ConfigureAwait(false)).ToList();
            }
            catch (McpProtocolException ex) when (IsUnsupportedMethod(ex))
            {
                Logger?.LogDebug("[MCP:{Endpoint}] prompts/list unsupported: {Message}", Config.Id, ex.Message);
                return _prompts = [];
            }
        }

        /// <summary>
        /// 判定协议错误是否表示「服务端不支持该列表方法」。
        /// 服务端实现不一致：可能返回 -32601，也可能用 -32602 表示能力缺失。
        /// </summary>
        private static bool IsUnsupportedMethod(McpProtocolException ex) =>
            ex.ErrorCode == McpErrorCode.MethodNotFound || ex.ErrorCode == McpErrorCode.InvalidParams;

        /// <summary>调用工具并格式化为文本。JSON-RPC 层错误直接抛出，工具自身的 isError 转为文本。</summary>
        public async Task<string> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct)
        {
            var arguments = McpJsonArguments.Parse(argumentsJson);
            var result = await _client.CallToolAsync(toolName, arguments, cancellationToken: ct).ConfigureAwait(false);
            return FormatToolResult(result, toolName);
        }

        /// <summary>读取资源并格式化为文本。</summary>
        public async Task<string> ReadResourceAsync(string uri, CancellationToken ct)
        {
            var result = await _client.ReadResourceAsync(uri, cancellationToken: ct).ConfigureAwait(false);
            return FormatResourceResult(result, uri);
        }

        /// <summary>获取提示词模板渲染结果。</summary>
        public async Task<GetPromptResult> GetPromptAsync(
            string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
        {
            return await _client.GetPromptAsync(name, arguments, cancellationToken: ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 释放失败必须留痕，但不向上抛出（调用方通常已处于收尾流程）。
                Logger?.LogWarning(ex, "[MCP:{Endpoint}] dispose failed: {Message}", Config.Id, ex.Message);
            }
        }

        /// <summary>把 CallToolResult 转为文本：文本块原样拼接，二进制块标注摘要，内嵌资源转为文本或标注。</summary>
        public static string FormatToolResult(CallToolResult result, string toolName)
        {
            var sb = new StringBuilder();
            if (result.IsError == true)
                sb.AppendLine($"[MCP tool '{toolName}' reported an error]");

            if (result.Content is { Count: > 0 })
            {
                foreach (var block in result.Content)
                    AppendContentBlock(sb, block);
            }

            if (result.StructuredContent is { } structured)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("structuredContent:");
                sb.AppendLine(structured.GetRawText());
            }

            if (sb.Length == 0)
                sb.Append($"[MCP tool '{toolName}' returned no content]");

            return sb.ToString().TrimEnd();
        }

        /// <summary>把 ReadResourceResult 转为文本：文本内容原样返回，二进制内容标注大小与类型。</summary>
        public static string FormatResourceResult(ReadResourceResult result, string uri)
        {
            var sb = new StringBuilder();
            foreach (var contents in result.Contents)
            {
                switch (contents)
                {
                    case TextResourceContents text:
                        sb.AppendLine(text.Text);
                        break;
                    case BlobResourceContents blob:
                        sb.AppendLine($"[binary resource {blob.Uri} ({blob.MimeType ?? "unknown type"}, {blob.DecodedData.Length} bytes) omitted]");
                        break;
                    default:
                        sb.AppendLine($"[unsupported resource contents {contents.GetType().Name} for {contents.Uri}]");
                        break;
                }
            }

            if (sb.Length == 0)
                sb.Append($"[resource '{uri}' returned no content]");

            return sb.ToString().TrimEnd();
        }

        private static void AppendContentBlock(StringBuilder sb, ContentBlock block)
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
                            sb.AppendLine($"[embedded binary resource {blob.Uri} ({blob.MimeType ?? "unknown type"}, {blob.DecodedData.Length} bytes) omitted]");
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
                    sb.AppendLine($"[image content omitted ({image.MimeType ?? "unknown type"}, {image.DecodedData.Length} bytes)]");
                    break;
                case AudioContentBlock audio:
                    sb.AppendLine($"[audio content omitted ({audio.MimeType ?? "unknown type"}, {audio.DecodedData.Length} bytes)]");
                    break;
                default:
                    sb.AppendLine($"[unsupported content block '{block.Type}' omitted]");
                    break;
            }
        }

        /// <summary>
        /// 宽松释放：SDK 的 IClientTransport / ITransport 释放接口并不统一，
        /// 这里按运行时能力选择 IAsyncDisposable 或 IDisposable。
        /// </summary>
        private static async ValueTask SafeDisposeAsync(object transport, ILoggerFactory? loggerFactory)
        {
            try
            {
                switch (transport)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                    default:
                        loggerFactory?.CreateLogger("ShimmerChat.Mcp").LogDebug(
                            "[MCP] transport {Type} is not disposable; nothing to clean up.",
                            transport.GetType().FullName);
                        break;
                }
            }
            catch (Exception)
            {
                // 连接建立失败后的清理：底层资源可能本就没有成功创建，忽略即可，
                // 原始连接异常才是需要向上报告的错误。
            }
        }
    }

    /// <summary>会话建立失败（进程启动、HTTP 握手、协议协商等）。</summary>
    public class McpConnectException : Exception
    {
        public McpConnectException(string message) : base(message) { }
        public McpConnectException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>MCP 客户端标识信息。</summary>
    public static class McpClientInfo
    {
        public static string Version =>
            typeof(McpClientInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    /// <summary>JSON 参数解析：把工具调用的 arguments 字符串解析为 SDK 需要的字典。</summary>
    internal static class McpJsonArguments
    {
        public static IReadOnlyDictionary<string, object> Parse(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)) return new Dictionary<string, object>();

            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(argumentsJson);
                if (node is not System.Text.Json.Nodes.JsonObject obj)
                    throw new McpConfigException(
                        $"MCP tool arguments must be a JSON object, got: {argumentsJson}");

                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var (key, value) in obj)
                {
                    if (value is null) continue;
                    // 原始 JSON 标量取出为 CLR 值，对象/数组保留 JsonNode：
                    // SDK 用 System.Text.Json 序列化，JsonNode 会被正确还原为 JSON。
                    result[key] = value is System.Text.Json.Nodes.JsonValue scalar
                        ? scalar.GetValue<object>()
                        : value;
                }
                return result;
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new McpConfigException($"MCP tool arguments are not valid JSON: {ex.Message}", ex);
            }
        }
    }
}
