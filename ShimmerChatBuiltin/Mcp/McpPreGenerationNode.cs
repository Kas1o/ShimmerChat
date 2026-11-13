using System.Text;
using Microsoft.Extensions.Logging;
using SharperLLM.Util;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Mcp
{
    /// <summary>节点级资源注入模式覆盖。</summary>
    public enum McpResourceInjectionMode
    {
        /// <summary>沿用端点配置。</summary>
        EndpointConfig = 0,

        /// <summary>本次生成不注入任何资源。</summary>
        Off = 1,

        /// <summary>按设置页中该端点各资源的勾选状态注入。</summary>
        Selected = 2,

        /// <summary>注入该端点列出的全部资源。</summary>
        All = 3
    }

    /// <summary>MCP 端点连接/枚举失败时的处理策略。</summary>
    public enum McpFailurePolicy
    {
        /// <summary>失败时中止本轮生成并报告错误（默认，行为与之前一致）。</summary>
        Fail = 0,

        /// <summary>失败时跳过该端点继续生成，同时写告警日志。</summary>
        Skip = 1
    }

    /// <summary>
    /// MCP 工具集前生成节点：应用「MCP 工具集设置页」中配置好的某个端点。
    /// <list type="bullet">
    /// <item>按端点配置/节点覆盖决定是否把 MCP 工具列表加入 <c>TransientEnv.Tools</c>；</item>
    /// <item>按端点配置/节点覆盖决定是否把 MCP 资源读入上下文片段；</item>
    /// <item>可选注入一个 <c>read_mcp_resource</c> 工具，让模型按需列举并读取资源；</item>
    /// <item>可选把服务端自报的 instructions 注入为 system 片段。</item>
    /// </list>
    /// 连接建立/枚举失败时的行为由 <see cref="FailurePolicy"/> 决定：默认返回明确的失败结果
    /// 由管线向上报告；也可配置为跳过该端点继续生成（同时写告警日志）。
    /// </summary>
    [NodeInfo("node.mcp_toolset", Icon = "🔌", Color = "var(--node-tool)",
        CategoryKeys = ["category.tool", "category.mcp"],
        DescriptionKey = "node.mcp_toolset.desc")]
    public class McpPreGenerationNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = "MCP Toolset";

        /// <summary>目标端点 Id（对应 MCP 工具集设置页中的标识）。</summary>
        [NodeProperty("prop.mcp_toolset.endpoint", HintKey = "prop.mcp_toolset.endpoint.hint",
            OptionsKey = "mcp_endpoints", OptionsSearchable = true, ShowOptionDescription = true,
            OptionsDefaultLabelKey = "prop.mcp_toolset.endpoint.default")]
        public string EndpointId { get; set; } = "";

        /// <summary>是否暴露该端点的工具列表。</summary>
        [NodeProperty("prop.mcp_toolset.expose_tools", HintKey = "prop.mcp_toolset.expose_tools.hint")]
        public bool ExposeTools { get; set; } = true;

        /// <summary>是否只暴露端点上显式勾选的工具（需先在设置页同步一次工具清单）。</summary>
        [NodeProperty("prop.mcp_toolset.tools_selected_only", HintKey = "prop.mcp_toolset.tools_selected_only.hint")]
        public bool ToolsSelectedOnly { get; set; }

        /// <summary>资源注入模式，覆盖端点配置。</summary>
        [NodeProperty("prop.mcp_toolset.resource_injection", HintKey = "prop.mcp_toolset.resource_injection.hint")]
        public McpResourceInjectionMode ResourceInjection { get; set; } = McpResourceInjectionMode.EndpointConfig;

        /// <summary>是否额外注入 <c>read_mcp_resource</c> 工具（让模型自行读取资源）。</summary>
        [NodeProperty("prop.mcp_toolset.expose_resource_tool", HintKey = "prop.mcp_toolset.expose_resource_tool.hint")]
        public bool ExposeResourceReadTool { get; set; }

        /// <summary>是否把端点的服务端标识与使用说明写入上下文。</summary>
        [NodeProperty("prop.mcp_toolset.inject_instructions", HintKey = "prop.mcp_toolset.inject_instructions.hint")]
        public bool InjectServerInstructions { get; set; } = true;

        /// <summary>连接/枚举失败时是中止本轮生成还是跳过该端点继续。</summary>
        [NodeProperty("prop.mcp_toolset.failure_policy", HintKey = "prop.mcp_toolset.failure_policy.hint")]
        public McpFailurePolicy FailurePolicy { get; set; } = McpFailurePolicy.Fail;

        public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var env = context.Env;
            var loc = env.Persistent.LocService;
            var ct = context.CancellationToken;

            if (string.IsNullOrWhiteSpace(EndpointId))
                return NodeResult.Failure(NodeErrorCodes.ParameterError,
                    loc["node_err.mcp_endpoint_empty"], nodeId: Id, nodeName: Name);

            List<McpEndpointConfig> endpoints;
            try
            {
                endpoints = McpEndpointCatalog.Load(env.Persistent.KVData);
            }
            catch (McpConfigException ex)
            {
                WriteDebug(env, $"endpoint catalog load failed: {ex.Message}");
                return NodeResult.Failure(NodeErrorCodes.DataMissing,
                    loc.Format("node_err.mcp_catalog_invalid", ex.Message), ex.ToString(), Id, Name);
            }

            if (endpoints.Count == 0)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc["node_err.mcp_no_endpoints"], nodeId: Id, nodeName: Name);

            var endpoint = McpEndpointCatalog.Find(endpoints, EndpointId);
            if (endpoint == null)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.mcp_endpoint_not_found", EndpointId), nodeId: Id, nodeName: Name);

            if (!endpoint.Enabled)
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound,
                    loc.Format("node_err.mcp_endpoint_disabled", endpoint.Name, endpoint.Id), nodeId: Id, nodeName: Name);

            var loggerFactory = env.Persistent.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;

            McpEndpointLease lease;
            try
            {
                lease = await McpRegistry.Instance.AcquireAsync(endpoint, loggerFactory, ct).ConfigureAwait(false);
            }
            catch (McpConfigException ex)
            {
                return NodeResult.Failure(NodeErrorCodes.ConfigNotFound, ex.Message, ex.ToString(), Id, Name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return NodeResult.Failure(NodeErrorCodes.Cancelled, loc["node_err.mcp_cancelled"], nodeId: Id, nodeName: Name);
            }
            catch (Exception ex)
            {
                return HandleRuntimeFailure(env, endpoint, NodeErrorCodes.ServiceError,
                    loc.Format("node_err.mcp_connect_failed", endpoint.Name, ex.Message), ex.ToString(),
                    $"endpoint '{endpoint.Id}' connect failed: {ex}");
            }

            try
            {
                return await ApplyEndpointAsync(env, endpoint, lease.Session, ct).ConfigureAwait(false);
            }
            finally
            {
                // 释放租约只减少引用计数；连接由注册表按空闲超时回收，
                // 因此同一对话的连续多轮生成不会重复握手。
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>把一个已连接的端点应用到本次生成：注册工具 + 注入资源上下文。</summary>
        private async Task<NodeResult> ApplyEndpointAsync(
            PreGenerationEnv env, McpEndpointConfig endpoint, McpEndpointSession session, CancellationToken ct)
        {
            var loc = env.Persistent.LocService;

            var addedTools = 0;
            var addedFragments = 0;

            var injection = ResolveResourceInjection(endpoint);

            // 预计算当前已暴露的工具名，避免在添加时反复调用 GetDefinition()（会重新解析 schema）。
            var existingToolNames = env.Transient.Tools
                .Select(t => t.GetDefinition().name)
                .ToHashSet(StringComparer.Ordinal);

            if (ExposeTools)
            {
                List<McpToolDescriptor> descriptors;
                try
                {
                    var tools = await session.GetToolsAsync(ct).ConfigureAwait(false);
                    descriptors = tools.Select(McpToolDescriptor.From).ToList();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return NodeResult.Failure(NodeErrorCodes.Cancelled, loc["node_err.mcp_cancelled"], nodeId: Id, nodeName: Name);
                }
                catch (Exception ex)
                {
                    return HandleRuntimeFailure(env, endpoint, NodeErrorCodes.ServiceError,
                        loc.Format("node_err.mcp_tools_failed", endpoint.Name, ex.Message), ex.ToString(),
                        $"endpoint '{endpoint.Id}' tools/list failed: {ex}");
                }

                addedTools += AddTools(env, session, endpoint, descriptors, ToolsSelectedOnly, existingToolNames);
            }

            if (injection != McpResourceInjection.Off)
            {
                try
                {
                    addedFragments += await AddResourceFragmentsAsync(env, session, endpoint, injection, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return NodeResult.Failure(NodeErrorCodes.Cancelled, loc["node_err.mcp_cancelled"], nodeId: Id, nodeName: Name);
                }
                catch (Exception ex)
                {
                    return HandleRuntimeFailure(env, endpoint, NodeErrorCodes.ServiceError,
                        loc.Format("node_err.mcp_resources_failed", endpoint.Name, ex.Message), ex.ToString(),
                        $"endpoint '{endpoint.Id}' resources/read failed: {ex}");
                }
            }

            if (ExposeResourceReadTool || endpoint.ExposeResourceReadTool)
                addedTools += AddResourceReadTool(env, session, existingToolNames);

            if (InjectServerInstructions)
                addedFragments += AddServerInstructionsFragment(env, session, endpoint);

            WriteLog(env, endpoint, McpLogLevel.Info,
                $"endpoint '{endpoint.Id}' applied: {addedTools} tool(s), {addedFragments} fragment(s), " +
                $"resource injection = {injection}.");

            return NodeResult.SuccessResult();
        }

        /// <summary>资源注入模式：节点覆盖 > 端点配置。</summary>
        private McpResourceInjection ResolveResourceInjection(McpEndpointConfig endpoint) => ResourceInjection switch
        {
            McpResourceInjectionMode.Off => McpResourceInjection.Off,
            McpResourceInjectionMode.All => McpResourceInjection.All,
            McpResourceInjectionMode.Selected => McpResourceInjection.Selected,
            _ => endpoint.ResourceInjection
        };

        /// <summary>
        /// 处理端点连接/枚举/读取的运行时失败：按 <see cref="FailurePolicy"/> 决定中止本轮还是跳过该端点。
        /// 取消与本地配置错误不走这里（它们始终显式返回）。
        /// </summary>
        private NodeResult HandleRuntimeFailure(PreGenerationEnv env, McpEndpointConfig endpoint,
            string errorCode, string userMessage, string details, string debugMessage)
        {
            WriteLog(env, endpoint, McpLogLevel.Error, debugMessage);

            if (FailurePolicy == McpFailurePolicy.Skip)
            {
                // 跳过后端点不再显式失败：降级必须可见，这里写一条不受 LogLevel 限制的告警。
                WriteDebug(env, $"endpoint '{endpoint.Id}' unavailable and skipped: {userMessage}");
                return NodeResult.SuccessResult();
            }

            return NodeResult.Failure(errorCode, userMessage, details, Id, Name);
        }

        private static int AddTools(PreGenerationEnv env, McpEndpointSession session,
            McpEndpointConfig endpoint, List<McpToolDescriptor> descriptors, bool selectedOnly,
            HashSet<string> existingNames)
        {
            var candidateNames = descriptors.Select(d => d.Name).ToList();

            IReadOnlyCollection<string> enabled;
            if (selectedOnly)
            {
                // 节点要求「仅勾选项」时，只暴露设置页中显式勾选的工具。
                enabled = descriptors
                    .Select(d => d.Name)
                    .Where(n => endpoint.Tools.Any(t => t.Enabled && string.Equals(t.Name, n, StringComparison.Ordinal)))
                    .ToList();
            }
            else
            {
                enabled = endpoint.ResolveEnabledTools(candidateNames).ToList();
            }

            var enabledSet = new HashSet<string>(enabled, StringComparer.Ordinal);
            var added = 0;

            foreach (var descriptor in descriptors)
            {
                if (!enabledSet.Contains(descriptor.Name)) continue;

                var exposedName = McpToolBridge.BuildExposedName(session.Config.Key, descriptor.Name);
                if (existingNames.Contains(exposedName))
                {
                    // sanitize 可能把不同工具名映射到同一暴露名；改名为唯一形式而不是静默丢弃。
                    exposedName = McpToolBridge.BuildUniqueExposedName(session.Config.Key, descriptor.Name, existingNames);
                    WriteLog(env, endpoint, McpLogLevel.Info, $"tool '{descriptor.Name}' exposed name collides; using '{exposedName}'.");
                }

                existingNames.Add(exposedName);
                env.Transient.Tools.Add(new McpToolBridge(session, descriptor, exposedName));
                added++;
            }

            return added;
        }

        private static int AddResourceReadTool(PreGenerationEnv env, McpEndpointSession session, HashSet<string> existingNames)
        {
            var bridge = new McpResourceReadToolBridge(session);
            if (!existingNames.Add(bridge.ExposedName))
                return 0;

            env.Transient.Tools.Add(bridge);
            return 1;
        }

        private static async Task<int> AddResourceFragmentsAsync(
            PreGenerationEnv env, McpEndpointSession session, McpEndpointConfig endpoint,
            McpResourceInjection injection, CancellationToken ct)
        {
            var resources = await session.GetResourcesAsync(ct).ConfigureAwait(false);
            if (resources.Count == 0) return 0;

            var available = resources.Select(r => r.Uri).ToList();
            var selected = injection == McpResourceInjection.All
                ? available
                : endpoint.ResolveEnabledResources(available).ToList();

            var byUri = resources.ToDictionary(r => r.Uri, StringComparer.Ordinal);
            var blocks = new List<(string Uri, string Rendered)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var uri in selected)
            {
                if (!seen.Add(uri)) continue;
                if (!byUri.TryGetValue(uri, out var resource)) continue;

                var content = await session.ReadResourceAsync(uri, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content)) continue;

                content = McpResources.Truncate(content, McpResources.PerResourceCharLimit, uri);
                blocks.Add((uri, McpResources.Render(endpoint.Name, endpoint.Id, uri, resource.Name, content)));
            }

            if (blocks.Count == 0) return 0;

            var (text, included, skipped) = McpResources.Compose(blocks, endpoint.ResourceCharBudget);
            if (string.IsNullOrWhiteSpace(text)) return 0;

            env.Transient.Fragments.Add(new ContextSegment
            {
                Message = new ChatMessage
                {
                    Content = $"## MCP resources ({endpoint.Name})\n\n{text}"
                },
                From = PromptBuilder.From.system,
                SourceType = typeof(McpPreGenerationNode),
                Metadata =
                {
                    ["mcp.endpoint"] = endpoint.Id,
                    ["mcp.resources.included"] = included,
                    ["mcp.resources.skipped"] = skipped
                }
            });

            return 1;
        }

        private static int AddServerInstructionsFragment(PreGenerationEnv env, McpEndpointSession session, McpEndpointConfig endpoint)
        {
            var instructions = session.ServerInstructions;
            if (string.IsNullOrWhiteSpace(instructions)) return 0;

            var sb = new StringBuilder();
            sb.AppendLine($"## MCP server instructions ({endpoint.Name})");
            if (session.ServerInfo != null)
                sb.AppendLine($"- server: {session.ServerInfo.Name} {session.ServerInfo.Version}");
            sb.AppendLine();
            sb.AppendLine(instructions);

            env.Transient.Fragments.Add(new ContextSegment
            {
                Message = new ChatMessage { Content = sb.ToString().TrimEnd() },
                From = PromptBuilder.From.system,
                SourceType = typeof(McpPreGenerationNode),
                Metadata = { ["mcp.endpoint"] = endpoint.Id }
            });

            return 1;
        }

        private static void WriteDebug(PreGenerationEnv env, string message)
        {
            env.Persistent.DebugOutput.Write(nameof(McpPreGenerationNode), "Mcp", $"[MCP] {message}");
        }

        /// <summary>按端点日志级别输出端点作用域内的日志。</summary>
        private static void WriteLog(PreGenerationEnv env, McpEndpointConfig endpoint, McpLogLevel level, string message)
        {
            if (!endpoint.LogsAt(level)) return;
            env.Persistent.DebugOutput.Write(nameof(McpPreGenerationNode), "Mcp", $"[MCP] {message}");
        }
    }
}
