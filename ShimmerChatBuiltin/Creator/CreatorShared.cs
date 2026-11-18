using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Creator
{
    /// <summary>
    /// Creator 工具集的共享逻辑：管线字符串解析、序列化器解析与节点树 JSON 校验。
    /// 树校验走对应管线的 <see cref="ITreeNodeSerializer"/>（类型白名单 + 旧格式懒迁移），
    /// 与运行期真正反序列化树的时代码是同一套，不复制第二份校验规则。
    /// </summary>
    internal static class CreatorShared
    {
        /// <summary>模型可用的管线取值（tool 参数与输出统一使用这三个短名）。</summary>
        public static readonly List<string> PipelineValues = ["pre", "post", "render"];

        /// <summary>
        /// 解析管线参数。大小写不敏感；未知值返回 false，由调用方转成显式失败，
        /// 不做隐式修补（截断/近似匹配会让模型误以为自己的参数生效了）。
        /// </summary>
        public static bool TryParsePipeline(string? value, out GenerationPipeline pipeline, out string error)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "pre":
                case "pre_generation":
                case "pregeneration":
                    pipeline = GenerationPipeline.PreGeneration;
                    error = "";
                    return true;
                case "post":
                case "post_generation":
                case "postgeneration":
                    pipeline = GenerationPipeline.PostGeneration;
                    error = "";
                    return true;
                case "render":
                case "render_modifier":
                case "rendermodifier":
                    pipeline = GenerationPipeline.RenderModifier;
                    error = "";
                    return true;
                default:
                    pipeline = GenerationPipeline.PreGeneration;
                    error = $"'pipeline' must be one of: {string.Join(", ", PipelineValues)}.";
                    return false;
            }
        }

        /// <summary>解析 agent GUID。空值或非法格式返回 false（不自动修正）。</summary>
        public static bool TryParseGuid(string? value, out Guid guid)
        {
            return Guid.TryParse(value?.Trim(), out guid) && guid != Guid.Empty;
        }

        /// <summary>
        /// 解析宿主的管线序列化器。当前上下文没有宿主服务（如子代理虚拟会话）时返回 null，
        /// 由调用方转成 <see cref="ToolErrorCodes.HostServiceUnavailable"/> 显式失败。
        /// </summary>
        public static ITreeNodeSerializer? ResolveSerializer(
            IServiceProvider? services, GenerationPipeline pipeline)
        {
            var serviceType = pipeline switch
            {
                GenerationPipeline.PreGeneration => typeof(IPreGenerationNodeSerializer),
                GenerationPipeline.PostGeneration => typeof(IPostGenerationNodeSerializerService),
                GenerationPipeline.RenderModifier => typeof(IRenderModifierNodeSerializer),
                _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
            };

            return services?.GetService(serviceType) as ITreeNodeSerializer;
        }
        /// <summary>
        /// 校验并规范化一棵节点树 JSON。成功时 <paramref name="normalizedJson"/> 是重新序列化后的
        /// 标准格式（旧格式经懒迁移后写回即转正，<paramref name="migratedFromLegacy"/> 为 true）；
        /// 失败时返回带错误码的 <see cref="ToolResult"/>。
        /// 空对象 "{}" 视为「没有树」，调用方按清除语义处理，这里不校验。
        /// </summary>
        public static ToolResult? ValidateTree(
            IServiceProvider? services, GenerationPipeline pipeline, string treeJson,
            out string normalizedJson, out bool migratedFromLegacy)
        {
            normalizedJson = treeJson;
            migratedFromLegacy = false;

            var serializer = ResolveSerializer(services, pipeline);
            if (serializer == null)
                return ToolResult.Fail(
                    ToolErrorCodes.HostServiceUnavailable,
                    $"The node tree serializer for pipeline '{pipeline}' is not available in this context. " +
                    "Pipeline tree operations require a host session.");

            try
            {
                var result = serializer.DeserializeTree(treeJson);
                if (!result.Success)
                    return ToolResult.Fail(
                        ToolErrorCodes.TreeJsonInvalid,
                        result.Error ?? "Node tree JSON did not deserialize.");

                if (result.Root == null)
                    return null; // "{}"/空白：合法但等于未配置，由调用方决定清除。

                // 重新序列化得到标准格式；旧格式 JSON 从此转为新格式存储。
                normalizedJson = serializer.Serialize(result.Root);
                migratedFromLegacy = result.Migrated;
                return null;
            }
            catch (Exception ex)
            {
                // 序列化器内部已捕获常规错误，这里兜住反射/白名单构建等意外并显式上报，不静默吞掉。
                return ToolResult.Fail(
                    ToolErrorCodes.TreeJsonInvalid,
                    "Node tree JSON failed validation.",
                    ex.ToString());
            }
        }

        /// <summary>模型可见的紧凑 JSON 输出（数据即结果，不拼提示词）。</summary>
        public static string Json(object data)
            => JsonConvert.SerializeObject(data, Formatting.None);

        /// <summary>
        /// 严格读取字符串数组参数：缺失时 value = null（「未提供」）；显式 null 返回空列表（「清空」）；
        /// 数组本身或任一元素类型不符返回 false（调用方必须显式失败）。
        /// </summary>
        public static bool TryGetStringArray(
            Newtonsoft.Json.Linq.JObject args, string name,
            out List<string>? value, out string? error)
        {
            if (!args.ContainsKey(name))
            {
                value = null;
                error = null;
                return true;
            }

            var token = args[name];
            if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null)
            {
                value = [];
                error = null;
                return true;
            }

            if (token.Type != Newtonsoft.Json.Linq.JTokenType.Array)
            {
                value = null;
                error = $"'{name}' must be an array of strings, got JSON type '{token.Type}'.";
                return false;
            }

            var values = new List<string>();
            foreach (var item in token)
            {
                if (item.Type != Newtonsoft.Json.Linq.JTokenType.String)
                {
                    value = null;
                    error = $"'{name}' must contain only strings; found an element of type '{item.Type}'.";
                    return false;
                }
                values.Add(item.Value<string>() ?? "");
            }

            value = values;
            error = null;
            return true;
        }

        /// <summary>
        /// 严格读取字符串参数：缺失 / 显式 null 都得到 <paramref name="value"/> = null，
        /// 但**类型不符**返回 false（调用方必须显式失败）。
        /// 静默把错误类型当成 null 会让模型误以为自己的参数已生效。
        /// </summary>
        public static bool TryGetStringArg(
            Newtonsoft.Json.Linq.JObject args, string name,
            out string? value, out string? error)
        {
            if (!args.ContainsKey(name))
            {
                value = null;
                error = null;
                return true;
            }

            var token = args[name];
            if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null)
            {
                value = null;
                error = null;
                return true;
            }

            if (token.Type != Newtonsoft.Json.Linq.JTokenType.String)
            {
                value = null;
                error = $"'{name}' must be a string, got JSON type '{token.Type}'.";
                return false;
            }

            value = token.Value<string>();
            error = null;
            return true;
        }
    }
}
