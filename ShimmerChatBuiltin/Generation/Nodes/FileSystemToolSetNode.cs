using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin.FileSystem;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Generation.Nodes
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

        [NodeProperty("prop.fs_tool_set.enable_write", Order = 3)]
        public bool EnableWrite { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_edit", Order = 4)]
        public bool EnableEdit { get; set; } = true;

        [NodeProperty("prop.fs_tool_set.enable_overview", Order = 5)]
        public bool EnableOverview { get; set; } = true;

        // -- 范围配置 --

        [NodeProperty("prop.fs_tool_set.allow_list", Order = 6, MultiLine = true)]
        public string AllowList { get; set; } = "";

        [NodeProperty("prop.fs_tool_set.ban_list", Order = 7, MultiLine = true)]
        public string BanList { get; set; } = "";

        [NodeProperty("prop.fs_tool_set.scope_mode", Order = 8)]
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
        /// </summary>
        private class ScopedFileSystemTool : IToolV2
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

            public async Task<string> ExecuteAsync(string input)
            {
                var path = TryExtractPath(input);
                if (string.IsNullOrEmpty(path))
                    return await _inner.ExecuteAsync(input);

                try
                {
                    path = Path.GetFullPath(path).Replace('\\', '/');
                }
                catch
                {
                    return $"Invalid path: '{path}'.";
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

                if (!allowed)
                    return $"Access denied by node scope: '{path}' is not allowed.";

                return await _inner.ExecuteAsync(input);
            }

            private static string? TryExtractPath(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return null;
                try
                {
                    return JObject.Parse(input).Value<string>("path");
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
