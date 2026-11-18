using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Creator
{
    /// <summary>
    /// Creator 工具集：管线预设管理。三条管线（前生成 / 后生成 / 渲染修改）的
    /// 可复用节点树模板的列出 / 查看 / 新建 / 修改 / 删除。
    /// <para>
    /// 存取一律经 <see cref="PipelinePresetStore"/>（存储位置的唯一事实来源）；
    /// 树 JSON 写入前经对应管线序列化器校验并规范化；
    /// "__default__" 预设由宿主管理器在每次启动时重建，拒绝通过本工具修改或删除。
    /// </para>
    /// </summary>
    public class CreatorPresetTool : IAutoCreateToolV2, IToolV3
    {
        /// <summary>预设列表是「读全表 → 改 → 整体写回」，进程内互斥防止并发写互相覆盖。</summary>
        private static readonly object PresetWriteLock = new();

        private const string DefaultPresetId = "__default__";

        private readonly IKVDataService _kvData;
        private readonly IDebugOutputService _debug;
        private readonly IServiceProvider? _services;

        public static string NameKey => "tool.creator_preset";
        public static string DescriptionKey => "tool.creator_preset.desc";
        public static string[] CategoryKeys => ["category.creator"];

        public CreatorPresetTool(PersistentEnv env)
        {
            _kvData = env.KVData;
            _debug = env.DebugOutput;
            _services = env.Services;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new CreatorPresetTool(env);

        public Tool GetDefinition() => new()
        {
            name = "creator_preset",
            description =
                "Manage reusable pipeline presets (node tree templates referenced by CallNode / " +
                "PostCallNode / RenderCallNode). Actions: 'list' {pipeline?} — presets of 'pre' | 'post' | " +
                "'render', all three when the filter is omitted; each entry has id, name and pinned. " +
                "'get' {pipeline, preset_id} — name, pinned and root_node_json. 'create' {pipeline, name, " +
                "root_node_json?, pinned?} — returns the new preset_id. 'update' {pipeline, preset_id, name?, " +
                "root_node_json?, pinned?} — only fields present in the arguments are changed; the tree is " +
                "validated first. 'delete' {pipeline, preset_id}. The '__default__' preset is managed by the " +
                "app and cannot be updated or deleted here. root_node_json uses the same wrapper node format " +
                "as agent pipeline trees (see creator_node_catalog).",
            parameters =
            [
                (new ToolParameter
                {
                    name = "action",
                    type = ParameterType.String,
                    description = "Operation to perform.",
                    @enum = ["list", "get", "create", "update", "delete"]
                }, true),
                (new ToolParameter
                {
                    name = "pipeline",
                    type = ParameterType.String,
                    description = "Pipeline the preset belongs to. Required for every action except 'list'.",
                    @enum = CreatorShared.PipelineValues
                }, false),                (new ToolParameter
                {
                    name = "preset_id",
                    type = ParameterType.String,
                    description = "Preset id (from 'list' or 'create')."
                }, false),
                (new ToolParameter
                {
                    name = "name",
                    type = ParameterType.String,
                    description = "Preset display name (create/update)."
                }, false),
                (new ToolParameter
                {
                    name = "root_node_json",
                    type = ParameterType.String,
                    description = "Node tree JSON (create/update); validated before storing."
                }, false),
                (new ToolParameter
                {
                    name = "pinned",
                    type = ParameterType.Boolean,
                    description = "Pin flag; pinned presets sort first in pickers (create/update)."
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

            if (string.IsNullOrEmpty(action))
                return Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'action' is required."));

            if (action is not ("list" or "get" or "create" or "update" or "delete"))
                return Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    $"Unknown action '{action}'."));

            // 'list' 以外都必须显式指定管线。
            var pipeline = GenerationPipeline.PreGeneration;
            if (action != "list")
            {
                if (!TryGetPipeline(args, out pipeline, out var pipelineError))
                    return Task.FromResult(pipelineError);
            }

            return Task.FromResult(action switch
            {
                "list" => ListPresets(args),
                "get" => GetTyped(pipeline, args),
                "create" => CreateTyped(pipeline, args),
                "update" => UpdateTyped(pipeline, args),
                "delete" => DeleteTyped(pipeline, args),
                _ => ToolResult.Fail(ToolErrorCodes.InvalidArguments, $"Unknown action '{action}'.")
            });
        }

        // ------------------------------------------------------------------
        // 按管线分派到具体预设类型；三个预设类结构一致，泛型方法只有一份实现。
        // ------------------------------------------------------------------

        private ToolResult GetTyped(GenerationPipeline pipeline, JObject args) => pipeline switch
        {
            GenerationPipeline.PreGeneration => GetTyped<PreGenerationPreset>(pipeline, args),
            GenerationPipeline.PostGeneration => GetTyped<PostGenerationPreset>(pipeline, args),
            GenerationPipeline.RenderModifier => GetTyped<RenderModifierPreset>(pipeline, args),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private ToolResult CreateTyped(GenerationPipeline pipeline, JObject args) => pipeline switch
        {
            GenerationPipeline.PreGeneration => CreateTyped<PreGenerationPreset>(pipeline, args),
            GenerationPipeline.PostGeneration => CreateTyped<PostGenerationPreset>(pipeline, args),
            GenerationPipeline.RenderModifier => CreateTyped<RenderModifierPreset>(pipeline, args),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private ToolResult UpdateTyped(GenerationPipeline pipeline, JObject args) => pipeline switch
        {
            GenerationPipeline.PreGeneration => UpdateTyped<PreGenerationPreset>(pipeline, args),
            GenerationPipeline.PostGeneration => UpdateTyped<PostGenerationPreset>(pipeline, args),
            GenerationPipeline.RenderModifier => UpdateTyped<RenderModifierPreset>(pipeline, args),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private ToolResult DeleteTyped(GenerationPipeline pipeline, JObject args) => pipeline switch
        {
            GenerationPipeline.PreGeneration => DeleteTyped<PreGenerationPreset>(pipeline, args),
            GenerationPipeline.PostGeneration => DeleteTyped<PostGenerationPreset>(pipeline, args),
            GenerationPipeline.RenderModifier => DeleteTyped<RenderModifierPreset>(pipeline, args),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        // ------------------------------------------------------------------
        // 列出 / 查看
        // ------------------------------------------------------------------

        private ToolResult ListPresets(JObject args)
        {
            var pipelines = new List<GenerationPipeline>();
            if (args.ContainsKey("pipeline"))
            {
                if (!CreatorShared.TryParsePipeline(
                        args["pipeline"]?.Value<string>(), out var pipeline, out var pipelineError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, pipelineError);
                pipelines.Add(pipeline);
            }
            else
            {
                pipelines.AddRange(Enum.GetValues<GenerationPipeline>());
            }

            var result = new Dictionary<string, object>();
            try
            {
                foreach (var pipeline in pipelines)
                {
                    var presets = ReadAll(pipeline);
                    result[ShortName(pipeline)] = presets
                        .Select(p => new { id = p.Id, name = p.Name, pinned = p.Pinned })
                        .ToArray();
                }
            }
            catch (JsonException ex)
            {
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    "A pipeline preset store is corrupt and could not be read.", ex.ToString());
            }

            return ToolResult.Ok(CreatorShared.Json(result));
        }

        private ToolResult GetTyped<TPreset>(GenerationPipeline pipeline, JObject args)
            where TPreset : class, IPipelinePreset
        {
            if (!TryGetPresetId(args, out var presetId, out var idError))
                return idError;

            List<TPreset> presets;
            try
            {
                presets = PipelinePresetStore.ReadAll<TPreset>(_kvData, pipeline);
            }
            catch (JsonException ex)
            {
                return StoreCorrupt<TPreset>(pipeline, ex);
            }

            var preset = presets.FirstOrDefault(p => p.Id == presetId);
            if (preset == null)
                return PresetNotFound(pipeline, presetId);

            return ToolResult.Ok(CreatorShared.Json(new
            {
                pipeline = ShortName(pipeline),
                preset_id = preset.Id,
                name = preset.Name,
                pinned = preset.Pinned,
                root_node_json = preset.RootNodeJson
            }));
        }

        // ------------------------------------------------------------------
        // 写操作
        // ------------------------------------------------------------------

        private ToolResult CreateTyped<TPreset>(GenerationPipeline pipeline, JObject args)
            where TPreset : class, IPipelinePreset, new()
        {
            if (!CreatorShared.TryGetStringArg(args, "name", out var name, out var nameError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, nameError!);
            if (string.IsNullOrWhiteSpace(name))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "'name' is required and must not be empty.");

            // 类型不符必须显式失败：静默当作空树会让模型误以为自己的树已写入。
            if (!CreatorShared.TryGetStringArg(args, "root_node_json", out var rawTree, out var treeError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, treeError!);

            string storedTree = "{}";
            bool migrated = false;

            if (!string.IsNullOrWhiteSpace(rawTree) && rawTree!.Trim() != "{}")
            {
                var validation = CreatorShared.ValidateTree(
                    _services, pipeline, rawTree, out var normalized, out migrated);
                if (validation != null)
                    return validation;
                storedTree = normalized;
            }

            var pinned = args.ContainsKey("pinned") && args["pinned"]?.Type == JTokenType.Boolean
                && args["pinned"]!.Value<bool>();

            var preset = new TPreset
            {
                Id = Guid.NewGuid().ToString(),
                Name = name!,
                Pinned = pinned,
                RootNodeJson = storedTree
            };

            try
            {
                lock (PresetWriteLock)
                {
                    var presets = PipelinePresetStore.ReadAll<TPreset>(_kvData, pipeline);
                    presets.Add(preset);
                    PipelinePresetStore.WriteAll(_kvData, pipeline, presets);
                }
            }
            catch (JsonException ex)
            {
                return StoreCorrupt<TPreset>(pipeline, ex);
            }

            _debug.Write("CreatorPresetTool", "Create",
                $"Preset '{name}' created for pipeline {ShortName(pipeline)} ({preset.Id})" +
                (migrated ? " from legacy tree JSON" : "") + ".");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                preset_id = preset.Id,
                name = preset.Name,
                pipeline = ShortName(pipeline),
                migrated_from_legacy = migrated
            }));
        }

        private ToolResult UpdateTyped<TPreset>(GenerationPipeline pipeline, JObject args)
            where TPreset : class, IPipelinePreset, new()
        {
            if (!TryGetPresetId(args, out var presetId, out var idError))
                return idError;

            if (presetId == DefaultPresetId)
                return ToolResult.Fail(ToolErrorCodes.ProtectedTarget,
                    "The '__default__' preset is regenerated by the app on every startup; " +
                    "changes made here would be lost. Create a new preset instead.");

            // 只应用参数里显式出现的字段；类型不符立即失败，不做静默修补。
            if (!CreatorShared.TryGetStringArg(args, "name", out var newName, out var nameError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, nameError!);
            if (newName != null && newName.Trim().Length == 0)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'name' must not be empty.");
            newName = newName?.Trim();

            bool? pinned = null;
            if (args.ContainsKey("pinned"))
            {
                if (args["pinned"]?.Type != JTokenType.Boolean)
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'pinned' must be a boolean.");
                pinned = args["pinned"]!.Value<bool>();
            }

            string? storedTree = null;
            bool migrated = false;
            bool treeProvided = args.ContainsKey("root_node_json");

            if (treeProvided)
            {
                // 类型不符必须显式失败：静默当作空树会让模型误以为自己的树已写入。
                if (!CreatorShared.TryGetStringArg(args, "root_node_json", out var rawTree, out var treeError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, treeError!);

                if (string.IsNullOrWhiteSpace(rawTree) || rawTree!.Trim() == "{}")
                {
                    storedTree = "{}"; // 显式空树：预设回到未配置状态
                }
                else
                {
                    var validation = CreatorShared.ValidateTree(
                        _services, pipeline, rawTree, out var normalized, out migrated);
                    if (validation != null)
                        return validation;
                    storedTree = normalized;
                }
            }

            if (newName == null && pinned == null && !treeProvided)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "No updatable fields present. Provide at least one of: name, root_node_json, pinned.");

            try
            {
                lock (PresetWriteLock)
                {
                    var presets = PipelinePresetStore.ReadAll<TPreset>(_kvData, pipeline);
                    var preset = presets.FirstOrDefault(p => p.Id == presetId);
                    if (preset == null)
                        return PresetNotFound(pipeline, presetId);

                    if (newName != null)
                        preset.Name = newName;
                    if (pinned != null)
                        preset.Pinned = pinned.Value;
                    if (treeProvided)
                        preset.RootNodeJson = storedTree!;

                    PipelinePresetStore.WriteAll(_kvData, pipeline, presets);
                }
            }
            catch (JsonException ex)
            {
                return StoreCorrupt<TPreset>(pipeline, ex);
            }

            _debug.Write("CreatorPresetTool", "Update",
                $"Preset '{presetId}' ({ShortName(pipeline)}) updated" +
                (migrated ? " with legacy tree JSON migrated" : "") + ".");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                preset_id = presetId,
                pipeline = ShortName(pipeline),
                status = "updated",
                migrated_from_legacy = treeProvided && migrated
            }));
        }

        private ToolResult DeleteTyped<TPreset>(GenerationPipeline pipeline, JObject args)
            where TPreset : class, IPipelinePreset, new()
        {
            if (!TryGetPresetId(args, out var presetId, out var idError))
                return idError;

            if (presetId == DefaultPresetId)
                return ToolResult.Fail(ToolErrorCodes.ProtectedTarget,
                    "The '__default__' preset is regenerated by the app on every startup and cannot be deleted.");

            try
            {
                lock (PresetWriteLock)
                {
                    var presets = PipelinePresetStore.ReadAll<TPreset>(_kvData, pipeline);
                    var preset = presets.FirstOrDefault(p => p.Id == presetId);
                    if (preset == null)
                        return PresetNotFound(pipeline, presetId);

                    presets.Remove(preset);
                    PipelinePresetStore.WriteAll(_kvData, pipeline, presets);
                }
            }
            catch (JsonException ex)
            {
                return StoreCorrupt<TPreset>(pipeline, ex);
            }

            _debug.Write("CreatorPresetTool", "Delete",
                $"Preset '{presetId}' deleted from pipeline {ShortName(pipeline)}.");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                preset_id = presetId,
                pipeline = ShortName(pipeline),
                status = "deleted"
            }));
        }

        // ------------------------------------------------------------------
        // 私有辅助
        // ------------------------------------------------------------------

        private List<IPipelinePreset> ReadAll(GenerationPipeline pipeline) => pipeline switch
        {
            GenerationPipeline.PreGeneration =>
                PipelinePresetStore.ReadAll<PreGenerationPreset>(_kvData, pipeline)
                    .Cast<IPipelinePreset>().ToList(),
            GenerationPipeline.PostGeneration =>
                PipelinePresetStore.ReadAll<PostGenerationPreset>(_kvData, pipeline)
                    .Cast<IPipelinePreset>().ToList(),
            GenerationPipeline.RenderModifier =>
                PipelinePresetStore.ReadAll<RenderModifierPreset>(_kvData, pipeline)
                    .Cast<IPipelinePreset>().ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private static ToolResult PresetNotFound(GenerationPipeline pipeline, string presetId)
            => ToolResult.Fail(ToolErrorCodes.TargetNotFound,
                $"No preset with id '{presetId}' in pipeline '{ShortName(pipeline)}'.");

        private static ToolResult StoreCorrupt<TPreset>(GenerationPipeline pipeline, JsonException ex)
            => ToolResult.Fail(ToolErrorCodes.ServiceError,
                $"The preset store for pipeline '{ShortName(pipeline)}' is corrupt; " +
                $"{typeof(TPreset).Name} entries could not be read.", ex.ToString());

        private static string ShortName(GenerationPipeline pipeline) => pipeline switch
        {
            GenerationPipeline.PreGeneration => "pre",
            GenerationPipeline.PostGeneration => "post",
            GenerationPipeline.RenderModifier => "render",
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private static bool TryGetPresetId(JObject args, out string presetId, out ToolResult failure)
        {
            presetId = args["preset_id"]?.Type == JTokenType.String
                ? args["preset_id"]!.Value<string>()!.Trim()
                : "";
            if (presetId.Length == 0)
            {
                failure = ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "'preset_id' is required and must not be empty.");
                return false;
            }

            failure = null!;
            return true;
        }

        private static bool TryGetPipeline(JObject args, out GenerationPipeline pipeline, out ToolResult failure)
        {
            if (!CreatorShared.TryParsePipeline(
                    args["pipeline"]?.Type == JTokenType.String ? args["pipeline"]!.Value<string>() : null,
                    out pipeline, out var error))
            {
                failure = ToolResult.Fail(ToolErrorCodes.InvalidArguments, error);
                return false;
            }

            failure = null!;
            return true;
        }
    }
}
