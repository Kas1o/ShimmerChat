using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Components;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Creator
{
    /// <summary>
    /// Creator 工具集：节点目录。把 <see cref="INodeTypeCatalog"/> 与 <see cref="IToolRegistry"/>
    /// 的自动发现结果以结构化数据暴露给模型：节点类型（即树 JSON 里的 "$type" 取值）、
    /// 本地化标签 / 描述、所属管线，以及每个 <c>[NodeProperty]</c> 属性的名称、类型、
    /// 枚举取值与选项来源——这是模型构建合法节点树 JSON 所需的全部元数据。
    /// </summary>
    public class CreatorNodeCatalogTool : IAutoCreateToolV2, IToolV3
    {
        private readonly IToolRegistry _toolRegistry;
        private readonly ILocService _loc;
        private readonly IServiceProvider? _services;

        public static string NameKey => "tool.creator_node_catalog";
        public static string DescriptionKey => "tool.creator_node_catalog.desc";
        public static string[] CategoryKeys => ["category.creator"];

        public CreatorNodeCatalogTool(PersistentEnv env)
        {
            _toolRegistry = env.ToolRegistry;
            _loc = env.LocService;
            _services = env.Services;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new CreatorNodeCatalogTool(env);

        public Tool GetDefinition() => new()
        {
            name = "creator_node_catalog",
            description =
                "List the building blocks for agent pipeline trees. " +
                "Action 'list' {pipeline?} returns every node type (filtered to 'pre' | 'post' | 'render' " +
                "when given) with: 'type' (the exact \"$type\" value used in tree JSON), localized label and " +
                "description, category, the pipelines it serves, and its editable properties (name, kind, " +
                "enum values, options key, multiline). Properties of kind 'child_list' hold nested node " +
                "wrappers: each array element uses the same wrapper shape " +
                "{\"Id\",\"Name\",\"IsExpanded\",\"IsBypassed\",\"Node\":{...}} as the tree root. Enum properties " +
                "are stored as their numeric value. Action 'list_tools' returns every auto-creatable tool with " +
                "its type name (used by tool presets and ToolInstantiateNode), localized name and categories. " +
                "Call this before building or editing tree JSON.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "action",
                    type = ParameterType.String,
                    description = "Operation to perform.",
                    @enum = ["list", "list_tools"]
                }, true),
                (new ToolParameter
                {
                    name = "pipeline",
                    type = ParameterType.String,
                    description = "Optional pipeline filter for the 'list' action.",
                    @enum = CreatorShared.PipelineValues
                }, false)
            ]
        };

        public Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var args = context.TryGetArgumentsObject();
            if (args == null)
                return Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    $"Arguments for tool '{context.ToolName}' are not a valid JSON object."));

            var action = args["action"]?.Type == JTokenType.String
                ? args["action"]!.Value<string>()!.Trim()
                : null;

            return Task.FromResult(action switch
            {
                "list" => ListNodes(args),
                "list_tools" => ListTools(),
                null or "" => ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'action' is required."),
                _ => ToolResult.Fail(ToolErrorCodes.InvalidArguments, $"Unknown action '{action}'.")
            });
        }

        // ------------------------------------------------------------------
        // 节点目录
        // ------------------------------------------------------------------

        private ToolResult ListNodes(JObject args)
        {
            var catalog = _services?.GetService(typeof(INodeTypeCatalog)) as INodeTypeCatalog;
            if (catalog == null)
                return ToolResult.Fail(ToolErrorCodes.HostServiceUnavailable,
                    "The node type catalog is not available in this context. " +
                    "Node listing requires a host session.");

            IEnumerable<NodeTypeMetadata> types;
            if (args.ContainsKey("pipeline"))
            {
                if (!CreatorShared.TryParsePipeline(
                        args["pipeline"]?.Value<string>(), out var pipeline, out var pipelineError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, pipelineError);

                types = catalog.GetNodeTypes(PipelinePresetStore.NodeInterfaceType(pipeline));
            }
            else
            {
                types = catalog.GetAllNodeTypes();
            }

            // 匿名类型的属性名即输出 JSON Key；排序键走元组，避免为排序另建可命名类型。
            var nodes = types
                .Select(meta => (Node: DescribeNode(meta), SortCategory: meta.FullCategory, SortType: meta.Type.FullName))
                .OrderBy(t => t.SortCategory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.SortType, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Node)
                .ToList();

            return ToolResult.Ok(CreatorShared.Json(new { count = nodes.Count, nodes }));
        }

        private object DescribeNode(NodeTypeMetadata meta)
        {
            return new
            {
                type = meta.Type.FullName, // 树 JSON 里 "$type" 使用的取值
                type_name = meta.Type.Name,
                label_key = meta.LabelKey,
                label = _loc[meta.LabelKey],
                description = meta.DescriptionKey != null ? _loc[meta.DescriptionKey] : null,
                category = meta.FullCategory,
                icon = meta.Icon,
                pipelines = PipelineNames(meta.Type),
                properties = DescribeProperties(meta.Type)
            };
        }

        private static string[] PipelineNames(Type nodeType)
        {
            var names = new List<string>();
            foreach (GenerationPipeline pipeline in Enum.GetValues(typeof(GenerationPipeline)))
            {
                if (PipelinePresetStore.NodeInterfaceType(pipeline).IsAssignableFrom(nodeType))
                    names.Add(pipeline switch
                    {
                        GenerationPipeline.PreGeneration => "pre",
                        GenerationPipeline.PostGeneration => "post",
                        GenerationPipeline.RenderModifier => "render",
                        _ => pipeline.ToString(),
                    });
            }
            return names.ToArray();
        }

        private object[] DescribeProperties(Type nodeType)
        {
            // 属性按 [NodeProperty].Order 排序；同序按名称，保证输出稳定可比对。
            return nodeType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(prop => (Prop: prop, Attr: prop.GetCustomAttribute<NodePropertyAttribute>()))
                .Where(pair => pair.Attr != null)
                .OrderBy(pair => pair.Attr!.Order)
                .ThenBy(pair => pair.Prop.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pair => DescribeProperty(pair.Prop, pair.Attr!))
                .ToArray();
        }

        private object DescribeProperty(PropertyInfo prop, NodePropertyAttribute attr)
        {
            string kind;
            object? elementWrapper = null;
            object? enumValues = null;

            if (TreeNodeReflection.IsListOfTreeNode(prop))
            {
                kind = "child_list";
                elementWrapper = TreeNodeReflection.GetTreeNodeElementType(prop.PropertyType)?.Name;
            }
            else if (TreeNodeReflection.IsSingleTreeNode(prop))
            {
                kind = "child_node";
            }
            else if (prop.PropertyType.IsEnum)
            {
                kind = "enum";
                enumValues = DescribeEnumValues(prop.PropertyType);
            }
            else
            {
                kind = prop.PropertyType switch
                {
                    var t when t == typeof(string) => "string",
                    var t when t == typeof(bool) => "bool",
                    var t when t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) => "int",
                    var t when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "float",
                    _ => prop.PropertyType.Name,
                };
            }

            return new
            {
                name = prop.Name,
                kind,
                label = _loc[attr.LabelKey],
                hint = attr.HintKey != null ? _loc[attr.HintKey] : null,
                options_key = attr.OptionsKey,
                multiline = attr.MultiLine,
                element_wrapper = elementWrapper,
                enum_values = enumValues
            };
        }

        /// <summary>
        /// 枚举取值同时给出名称与数字值：节点树 JSON 里枚举按 Newtonsoft 默认行为
        /// 存为数字（序列化器未配置 StringEnumConverter），模型需要数字值才能写出合法 JSON。
        /// </summary>
        private static object[] DescribeEnumValues(Type enumType)
        {
            var underlying = Enum.GetUnderlyingType(enumType);
            return Enum.GetNames(enumType)
                .Select(name => new
                {
                    name,
                    value = Convert.ChangeType(Enum.Parse(enumType, name), underlying)
                })
                .ToArray();
        }

        // ------------------------------------------------------------------
        // 工具目录
        // ------------------------------------------------------------------

        private ToolResult ListTools()
        {
            var tools = _toolRegistry.AllTools
                .Select(meta => new
                {
                    name_key = meta.NameKey,
                    name = _loc[meta.NameKey],
                    description = _loc[meta.DescriptionKey],
                    type_name = meta.TypeName, // ToolPresetNode / ToolInstantiateNode 使用的取值
                    categories = meta.CategoryKeys.Select(c => _loc[c]).ToArray()
                })
                .OrderBy(t => t.type_name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ToolResult.Ok(CreatorShared.Json(new { count = tools.Length, tools }));
        }
    }
}
