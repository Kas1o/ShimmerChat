using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin.FileSystem;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ToolManage
{
    /// <summary>
    /// 文件系统范围合并模式：与全局配置文件系统访问范围的组合方式
    /// </summary>
    public enum FileScopeMergeMode
    {
        Override,       // 覆盖（默认）：使用节点配置替换全局配置
        Union,          // 并集：节点和全局配置中任一允许即可
        Intersection    // 交集：必须同时被节点和全局配置允许
    }

    /// <summary>
    /// 文件系统工具集节点。可在节点中按需勾选文件系统工具，
    /// 并配置文件访问范围（AllowList/BanList），支持与全局配置进行 覆盖/并集/交集 合并。
    /// </summary>
    [NodeInfo("node.file_system_tool_set", Icon = "📁", Color = "var(--node-config)",
        CategoryKeys = ["category.tool", "category.preset"],
        DescriptionKey = "node.file_system_tool_set.desc")]
    public class FileSystemToolSetNode : IPreGenerationNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "FileSystem Tools";

        // -- 工具勾选 --

        [NodeProperty("prop.fs_tool_set.enable_browse", Order = 1)]
        public bool EnableBrowse { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_read", Order = 2)]
        public bool EnableRead { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_image_read", Order = 3)]
        public bool EnableImageRead { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_write", Order = 4)]
        public bool EnableWrite { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_edit", Order = 5)]
        public bool EnableEdit { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_overview", Order = 6)]
        public bool EnableOverview { get; set; } = true;

        // -- 范围配置 --

        [NodeProperty("prop.fs_tool_set.allow_list", Order = 7, MultiLine = true)]
        public string AllowList { get; set; } = "";

        [NodeProperty("prop.fs_tool_set.ban_list", Order = 8, MultiLine = true)]
        public string BanList { get; set; } = "";

        [NodeProperty("prop.fs_tool_set.scope_mode", Order = 9)]
        public FileScopeMergeMode ScopeMode { get; set; } = FileScopeMergeMode.Override;

        public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
        {
            var registry = context.Env.Persistent.ToolRegistry;
            var persistent = context.Env.Persistent;

            var nodeConfig = new FileSystemConfig
            {
                AllowList = ParseLines(AllowList),
                BanList = ParseLines(BanList)
            };

            if (EnableBrowse) AddScopedTool(registry, FileSystemBrowseToolV2.NameKey, persistent, nodeConfig, context);
            if (EnableRead) AddScopedTool(registry, FileSystemReadToolV2.NameKey, persistent, nodeConfig, context);
            if (EnableImageRead) AddScopedTool(registry, FileSystemImageReadToolV2.NameKey, persistent, nodeConfig, context);
            if (EnableWrite) AddScopedTool(registry, FileSystemWriteToolV2.NameKey, persistent, nodeConfig, context);
            if (EnableEdit) AddScopedTool(registry, FileSystemEditToolV2.NameKey, persistent, nodeConfig, context);
            if (EnableOverview) AddScopedTool(registry, FileSystemOverviewToolV2.NameKey, persistent, nodeConfig, context);

            return Task.FromResult(NodeResult.SuccessResult());
        }

        private void AddScopedTool(IToolRegistry registry, string nameKey, PersistentEnv persistent,
            FileSystemConfig nodeConfig, PreNodeExecutionContext context)
        {
            var meta = registry.FindByName(nameKey);
            if (meta == null) return;

            var tool = registry.CreateInstance(meta.Type, persistent);
            if (tool == null) return;

            // 概览工具没有路径参数，无需范围包装
            if (nameKey == FileSystemOverviewToolV2.NameKey)
            {
                context.Env.Transient.Tools.Add(tool);
            }
            else
            {
                context.Env.Transient.Tools.Add(
                    new ScopedFileSystemTool(tool, nodeConfig, ScopeMode, persistent.KVData));
            }
        }

        private static List<string> ParseLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];
            return text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 文件系统工具的作用域包装器。
        /// 在执行时根据合并模式检查路径是否允许，然后再委托给原始工具。
        /// <para>
        /// 同时实现 <see cref="IToolV3"/>：结构化工具（如 read_image）必须连同取消令牌和
        /// <see cref="ToolResult.Images"/> 一起被转发，否则包装层会把图像结果吞成纯文本。
        /// 旧工具仍走 <see cref="IToolV2"/> 的字符串路径。
        /// </para>
        /// </summary>
        private class ScopedFileSystemTool : IToolV2, IToolV3
        {
            private readonly IToolV2 _inner;
            private readonly FileSystemConfig _nodeConfig;
            private readonly FileScopeMergeMode _scopeMode;
            private readonly IKVDataService _kvData;

            public ScopedFileSystemTool(IToolV2 inner, FileSystemConfig nodeConfig,
                FileScopeMergeMode scopeMode, IKVDataService kvData)
            {
                _inner = inner;
                _nodeConfig = nodeConfig;
                _scopeMode = scopeMode;
                _kvData = kvData;
            }

            public Tool GetDefinition() => _inner.GetDefinition();

            public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
            {
                var (code, message) = CheckPathScope(context.ArgumentsJson);
                if (code != null)
                {
                    // 旧工具返回字符串，宿主一直把它当作成功文本；保持这一语义，
                    // 结构化工具则给出真正的失败，让节点按 ContinueOnError 决定是否中止。
                    return _inner is IToolV3
                        ? ToolResult.Fail(code, message!)
                        : ToolResult.Ok(message);
                }

                if (_inner is IToolV3 v3)
                    return await v3.ExecuteAsync(context, ct);

                var text = await _inner.ExecuteAsync(context.ArgumentsJson);
                return ToolResult.Ok(text);
            }

            public async Task<string> ExecuteAsync(string input)
            {
                var (code, message) = CheckPathScope(input);
                if (code != null)
                    return message!;

                if (_inner is IToolV3 v3)
                {
                    var result = await v3.ExecuteAsync(new ToolInvocationContext
                    {
                        ToolCallId = string.Empty,
                        ToolName = GetDefinition().name,
                        ArgumentsJson = input,
                        RoundIndex = 0
                    }, CancellationToken.None);

                    return result.Status == ToolResultStatus.Completed
                        ? result.Content ?? string.Empty
                        : $"[Tool error] {result.ErrorCode ?? ToolErrorCodes.ServiceError}: {result.ErrorMessage}";
                }

                return await _inner.ExecuteAsync(input);
            }

            /// <summary>
            /// 返回 null 表示放行；否则给出明确的错误码与信息。没有 path 参数的工具
            /// （如 file_system_overview）不参与路径范围检查。
            /// </summary>
            private (string? Code, string? Message) CheckPathScope(string input)
            {
                var path = TryExtractPath(input);
                if (string.IsNullOrEmpty(path))
                    return (null, null);

                try
                {
                    path = Path.GetFullPath(path).Replace('\\', '/');
                }
                catch
                {
                    return (ToolErrorCodes.InvalidArguments, $"Invalid path: '{path}'.");
                }

                var globalConfig = FileSystemConfigManager.Load(_kvData);

                bool allowed = _scopeMode switch
                {
                    FileScopeMergeMode.Override =>
                        FileSystemConfigManager.IsPathAllowed(path, _nodeConfig),

                    FileScopeMergeMode.Union =>
                        FileSystemConfigManager.IsPathAllowed(path, _nodeConfig)
                        || FileSystemConfigManager.IsPathAllowed(path, globalConfig),

                    FileScopeMergeMode.Intersection =>
                        FileSystemConfigManager.IsPathAllowed(path, _nodeConfig)
                        && FileSystemConfigManager.IsPathAllowed(path, globalConfig),

                    _ => FileSystemConfigManager.IsPathAllowed(path, _nodeConfig)
                };

                return allowed
                    ? (null, null)
                    : (ToolErrorCodes.AccessDenied,
                        $"Access denied by node scope: '{path}' is not allowed.");
            }

            private static string? TryExtractPath(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return null;
                try
                {
                    return JObject.Parse(input).Value<string?>("path");
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
