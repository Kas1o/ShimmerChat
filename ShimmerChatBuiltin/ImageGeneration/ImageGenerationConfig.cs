using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>自定义工作流工具参数的类型。与 Function Calling 的 JSON 类型一一对应。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ImageWorkflowParameterType
    {
        String = 0,
        Integer = 1,
        Number = 2,
        Boolean = 3,
        Enum = 4
    }

    /// <summary>
    /// 自定义工作流声明的一个工具参数。工具定义、模板占位符替换与参数校验都以它为准，
    /// 不在别处维护第二份形参表。
    /// </summary>
    public sealed class ImageWorkflowParameter
    {
        /// <summary>参数名，同时是模板占位符名（<c>{{name}}</c>）。</summary>
        public string Name { get; set; } = "";

        public ImageWorkflowParameterType Type { get; set; } = ImageWorkflowParameterType.String;

        /// <summary>给模型看的参数说明。</summary>
        public string Description { get; set; } = "";

        /// <summary>是否必填。非必填参数必须提供可解析的默认值。</summary>
        public bool Required { get; set; }

        /// <summary>默认值（按 <see cref="Type"/> 解析；String 允许空串）。</summary>
        public string DefaultValue { get; set; } = "";

        /// <summary>Enum 类型允许的取值。</summary>
        public List<string> EnumValues { get; set; } = [];

        /// <summary>数值下限（含），仅 Integer / Number 使用。</summary>
        public double? Min { get; set; }

        /// <summary>数值上限（含），仅 Integer / Number 使用。</summary>
        public double? Max { get; set; }
    }

    /// <summary>
    /// 用户在设置面板中声明的自定义 ComfyUI 工作流。
    /// <see cref="WorkflowJson"/> 是 API 格式的工作流 JSON，其中的 <c>{{参数名}}</c>
    /// 由 <see cref="WorkflowTemplateEngine"/> 替换。
    /// </summary>
    public sealed class CustomImageWorkflow
    {
        /// <summary>持久化标识，也用于生成工具名：<c>generate_image_{Id}</c>。</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>给模型看的工具说明补充。</summary>
        public string Description { get; set; } = "";

        /// <summary>只有启用的工作流才会被工具集节点暴露给模型；停用时允许是未完成的草稿。</summary>
        public bool Enabled { get; set; } = true;

        public string WorkflowJson { get; set; } = "";

        public List<ImageWorkflowParameter> Parameters { get; set; } = [];
    }

    /// <summary>默认工作流的可调参数。工具调用可以逐个覆盖，未覆盖时使用这里的值。</summary>
    public sealed class DefaultImageWorkflowSettings
    {
        /// <summary>CheckpointLoaderSimple 的 ckpt_name，例如 <c>sd_xl_base_1.0.safetensors</c>。</summary>
        public string Model { get; set; } = "";

        public int Width { get; set; } = 512;

        public int Height { get; set; } = 512;

        public int Steps { get; set; } = 20;

        public double CfgScale { get; set; } = 7.0;

        public string Sampler { get; set; } = "euler";

        public string Scheduler { get; set; } = "normal";

        public string NegativePrompt { get; set; } = "";

        public int BatchSize { get; set; } = 1;
    }

    /// <summary>文生图全局配置（KVData "ImageGeneration/Config"）。</summary>
    public sealed class ImageGenerationConfig
    {
        /// <summary>ComfyUI 服务根地址，例如 <c>http://127.0.0.1:8188</c>。</summary>
        public string ComfyUiBaseUrl { get; set; } = "http://127.0.0.1:8188";

        /// <summary>单次 HTTP 请求超时（秒）。</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>从提交任务到拿到结果的总体等待上限（秒）。</summary>
        public int MaxWaitSeconds { get; set; } = 300;

        /// <summary>轮询 /history 的间隔（毫秒）。</summary>
        public int PollIntervalMilliseconds { get; set; } = 1000;

        public DefaultImageWorkflowSettings DefaultWorkflow { get; set; } = new();

        public List<CustomImageWorkflow> CustomWorkflows { get; set; } = [];

        /// <summary>查找启用的自定义工作流；返回 null 表示没有配置该 Id。</summary>
        public CustomImageWorkflow? FindCustomWorkflow(string id) =>
            CustomWorkflows.FirstOrDefault(
                w => string.Equals(w.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// 参数定义解析规则。设置面板校验、模板引擎绑定和默认值解析共用这一份实现，
    /// 避免 UI 与执行期对「什么算合法默认值」给出不同答案。
    /// </summary>
    public static class ImageWorkflowParameterRules
    {
        public static readonly Regex NamePattern = new(
            "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>把文本按参数类型解析成强类型值。失败时返回错误说明，不做猜测性转换。</summary>
        public static (object? Value, string? Error) ParseText(ImageWorkflowParameter parameter, string? text)
        {
            var raw = text?.Trim() ?? "";

            switch (parameter.Type)
            {
                case ImageWorkflowParameterType.String:
                    return (text ?? "", null);

                case ImageWorkflowParameterType.Enum:
                {
                    if (parameter.EnumValues == null || parameter.EnumValues.Count == 0)
                        return (null, $"Enum parameter '{parameter.Name}' declares no enum values.");

                    var match = parameter.EnumValues.FirstOrDefault(
                        v => string.Equals(v, raw, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                        return (null, $"Parameter '{parameter.Name}' must be one of: " +
                                      $"{string.Join(", ", parameter.EnumValues)}; got '{text}'.");

                    return (match, null);
                }

                case ImageWorkflowParameterType.Integer:
                {
                    if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                        return (null, $"Parameter '{parameter.Name}' must be an integer; got '{text}'.");

                    var rangeError = ValidateRange(parameter, value);
                    return rangeError == null ? (value, null) : (null, rangeError);
                }

                case ImageWorkflowParameterType.Number:
                {
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                        || !double.IsFinite(value))
                    {
                        return (null, $"Parameter '{parameter.Name}' must be a finite number; got '{text}'.");
                    }

                    var rangeError = ValidateRange(parameter, value);
                    return rangeError == null ? (value, null) : (null, rangeError);
                }

                case ImageWorkflowParameterType.Boolean:
                {
                    if (!bool.TryParse(raw, out var value))
                        return (null, $"Parameter '{parameter.Name}' must be true or false; got '{text}'.");

                    return (value, null);
                }

                default:
                    return (null, $"Parameter '{parameter.Name}' has unsupported type {parameter.Type}.");
            }
        }

        /// <summary>校验参数定义本身（类型、默认值、范围）；名称与重复性由调用方检查。</summary>
        public static string? ValidateDefinition(ImageWorkflowParameter parameter)
        {
            if (parameter.Name == null || !NamePattern.IsMatch(parameter.Name))
            {
                return $"Parameter name '{parameter.Name}' is invalid. " +
                       "Use letters, digits and underscores; the first character must be a letter or underscore.";
            }

            if (parameter.Description == null)
                return $"Parameter '{parameter.Name}' description must not be null.";

            if (parameter.Type == ImageWorkflowParameterType.Enum)
            {
                if (parameter.EnumValues == null || parameter.EnumValues.Count == 0)
                    return $"Enum parameter '{parameter.Name}' must declare at least one value.";

                if (parameter.EnumValues.Any(string.IsNullOrWhiteSpace))
                    return $"Enum parameter '{parameter.Name}' contains an empty value.";

                if (parameter.EnumValues.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != parameter.EnumValues.Count)
                {
                    return $"Enum parameter '{parameter.Name}' contains duplicate values (case-insensitive).";
                }
            }
            else if (parameter.EnumValues is { Count: > 0 })
            {
                return $"Parameter '{parameter.Name}' is not an Enum but declares enum values.";
            }

            if (parameter.Min.HasValue || parameter.Max.HasValue)
            {
                if (parameter.Type is not (ImageWorkflowParameterType.Integer or ImageWorkflowParameterType.Number))
                    return $"Parameter '{parameter.Name}' declares Min/Max but is not numeric.";

                if (!double.IsFinite(parameter.Min ?? 0) || !double.IsFinite(parameter.Max ?? 0))
                    return $"Parameter '{parameter.Name}' Min/Max must be finite numbers.";

                if (parameter.Min.HasValue && parameter.Max.HasValue && parameter.Min > parameter.Max)
                    return $"Parameter '{parameter.Name}' Min must be <= Max.";
            }

            // 必填参数在调用时一定来自模型，默认值不参与解析；非必填参数缺省时必须能解析。
            if (!parameter.Required)
            {
                var (_, error) = ParseText(parameter, parameter.DefaultValue);
                if (error != null)
                    return error;
            }

            return null;
        }

        private static string? ValidateRange(ImageWorkflowParameter parameter, double value)
        {
            if (parameter.Min.HasValue && value < parameter.Min.Value)
                return $"Parameter '{parameter.Name}' must be >= {parameter.Min.Value.ToString(CultureInfo.InvariantCulture)}; got {value.ToString(CultureInfo.InvariantCulture)}.";

            if (parameter.Max.HasValue && value > parameter.Max.Value)
                return $"Parameter '{parameter.Name}' must be <= {parameter.Max.Value.ToString(CultureInfo.InvariantCulture)}; got {value.ToString(CultureInfo.InvariantCulture)}.";

            return null;
        }
    }

    /// <summary>ImageGenerationConfig 的 KVData 读写与集中校验。</summary>
    public static class ImageGenerationConfigManager
    {
        public const string SpaceId = "ImageGeneration";
        public const string ConfigKey = "Config";

        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 600;

        public const int MinMaxWaitSeconds = 1;
        public const int MaxMaxWaitSeconds = 3600;

        public const int MinPollIntervalMilliseconds = 100;
        public const int MaxPollIntervalMilliseconds = 60_000;

        public const int MinDimension = 64;
        public const int MaxDimension = 8192;

        public const int MinSteps = 1;
        public const int MaxSteps = 150;

        public const double MinCfgScale = 0;
        public const double MaxCfgScale = 100;

        public const int MinBatchSize = 1;
        public const int MaxBatchSize = 8;

        /// <summary>自定义工作流 Id：直接拼进工具名，因此限定为安全字符。</summary>
        public static readonly Regex WorkflowIdPattern = new(
            "^[A-Za-z0-9_-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 读取配置。未写入过时返回默认配置；JSON 损坏或取值非法时返回错误说明，
        /// 由调用方决定如何汇报（不静默回落成默认值）。
        /// </summary>
        public static (ImageGenerationConfig? Config, string? Error) Load(IKVDataService kvData)
        {
            var json = kvData.Read(SpaceId, ConfigKey);
            if (string.IsNullOrWhiteSpace(json))
                return (new ImageGenerationConfig(), null);

            ImageGenerationConfig? config;
            try
            {
                config = JsonConvert.DeserializeObject<ImageGenerationConfig>(json);
            }
            catch (JsonException ex)
            {
                return (null, $"Image generation config is not valid JSON: {ex.Message}");
            }

            if (config == null)
                return (null, "Image generation config deserialized to null.");

            var error = Validate(config);
            return error == null ? (config, null) : (null, error);
        }

        /// <summary>
        /// 校验配置取值。设置面板保存与 KVData 读取共用同一判定，避免两边规则不一致。
        /// 返回错误说明，null 表示合法。
        /// </summary>
        public static string? Validate(ImageGenerationConfig config)
        {
            var baseUrl = config.ComfyUiBaseUrl?.Trim();
            if (string.IsNullOrEmpty(baseUrl))
                return "ComfyUiBaseUrl must not be empty.";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return $"ComfyUiBaseUrl must be an absolute http(s) URL, got '{config.ComfyUiBaseUrl}'.";
            }

            if (config.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                return $"TimeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}, got {config.TimeoutSeconds}.";

            if (config.MaxWaitSeconds is < MinMaxWaitSeconds or > MaxMaxWaitSeconds)
                return $"MaxWaitSeconds must be between {MinMaxWaitSeconds} and {MaxMaxWaitSeconds}, got {config.MaxWaitSeconds}.";

            if (config.PollIntervalMilliseconds is < MinPollIntervalMilliseconds or > MaxPollIntervalMilliseconds)
            {
                return $"PollIntervalMilliseconds must be between {MinPollIntervalMilliseconds} and " +
                       $"{MaxPollIntervalMilliseconds}, got {config.PollIntervalMilliseconds}.";
            }

            if (config.DefaultWorkflow == null)
                return "DefaultWorkflow must not be null.";

            var defaultError = ValidateDefaultWorkflow(config.DefaultWorkflow);
            if (defaultError != null)
                return defaultError;

            if (config.CustomWorkflows == null)
                return "CustomWorkflows must be a list.";

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var workflow in config.CustomWorkflows)
            {
                var error = ValidateCustomWorkflow(workflow, ids, names);
                if (error != null)
                    return error;
            }

            return null;
        }

        public static void Save(IKVDataService kvData, ImageGenerationConfig config)
        {
            kvData.Write(SpaceId, ConfigKey, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private static string? ValidateDefaultWorkflow(DefaultImageWorkflowSettings settings)
        {
            if (settings.Width is < MinDimension or > MaxDimension)
                return $"DefaultWorkflow.Width must be between {MinDimension} and {MaxDimension}, got {settings.Width}.";

            if (settings.Width % 8 != 0)
                return $"DefaultWorkflow.Width must be a multiple of 8, got {settings.Width}.";

            if (settings.Height is < MinDimension or > MaxDimension)
                return $"DefaultWorkflow.Height must be between {MinDimension} and {MaxDimension}, got {settings.Height}.";

            if (settings.Height % 8 != 0)
                return $"DefaultWorkflow.Height must be a multiple of 8, got {settings.Height}.";

            if (settings.Steps is < MinSteps or > MaxSteps)
                return $"DefaultWorkflow.Steps must be between {MinSteps} and {MaxSteps}, got {settings.Steps}.";

            if (!double.IsFinite(settings.CfgScale) || settings.CfgScale is < MinCfgScale or > MaxCfgScale)
                return $"DefaultWorkflow.CfgScale must be between {MinCfgScale} and {MaxCfgScale}, got {settings.CfgScale}.";

            if (settings.BatchSize is < MinBatchSize or > MaxBatchSize)
                return $"DefaultWorkflow.BatchSize must be between {MinBatchSize} and {MaxBatchSize}, got {settings.BatchSize}.";

            if (string.IsNullOrWhiteSpace(settings.Sampler))
                return "DefaultWorkflow.Sampler must not be empty.";

            if (string.IsNullOrWhiteSpace(settings.Scheduler))
                return "DefaultWorkflow.Scheduler must not be empty.";

            // Model 允许为空：用户可能还没启动 ComfyUI 或安装 checkpoint。
            // 执行默认工作流时由工具给出明确失败，而不是在这里阻止保存其他设置。
            return null;
        }

        private static string? ValidateCustomWorkflow(
            CustomImageWorkflow workflow, HashSet<string> ids, HashSet<string> names)
        {
            if (workflow == null)
                return "CustomWorkflows contains a null entry.";

            if (workflow.Id == null || !WorkflowIdPattern.IsMatch(workflow.Id))
            {
                return $"Custom workflow id '{workflow.Id}' is invalid. " +
                       "Use 1-40 characters: letters, digits, underscore or hyphen.";
            }

            if (!ids.Add(workflow.Id))
                return $"Duplicate custom workflow id '{workflow.Id}'.";

            if (string.IsNullOrWhiteSpace(workflow.Name))
                return $"Custom workflow '{workflow.Id}' must have a name.";

            if (!names.Add(workflow.Name))
                return $"Duplicate custom workflow name '{workflow.Name}'.";

            // 停用的工作流是草稿：允许 JSON / 参数尚未写完，避免编辑过程中无法保存其他配置。
            if (!workflow.Enabled)
                return null;

            if (workflow.Parameters == null)
                return $"Custom workflow '{workflow.Id}' Parameters must be a list.";

            var (foundPlaceholders, placeholderError) =
                WorkflowTemplateEngine.FindPlaceholders(workflow.WorkflowJson ?? "");
            if (placeholderError != null)
                return $"Custom workflow '{workflow.Id}' template is invalid: {placeholderError}";

            var placeholders = foundPlaceholders!;
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in workflow.Parameters)
            {
                if (parameter == null)
                    return $"Custom workflow '{workflow.Id}' contains a null parameter.";

                if (!parameterNames.Add(parameter.Name))
                    return $"Custom workflow '{workflow.Id}' declares duplicate parameter '{parameter.Name}'.";

                var definitionError = ImageWorkflowParameterRules.ValidateDefinition(parameter);
                if (definitionError != null)
                    return $"Custom workflow '{workflow.Id}': {definitionError}";
            }

            var undeclared = placeholders.Where(p => !parameterNames.Contains(p)).ToList();
            if (undeclared.Count > 0)
            {
                return $"Custom workflow '{workflow.Id}' uses undeclared placeholder(s): " +
                       $"{string.Join(", ", undeclared)}.";
            }

            var renderError = ValidateTemplateIsRenderable(workflow);
            if (renderError != null)
                return renderError;

            var unused = parameterNames.Where(p => !placeholders.Contains(p)).ToList();
            if (unused.Count > 0)
            {
                return $"Custom workflow '{workflow.Id}' declares parameter(s) that never appear " +
                       $"in the workflow template: {string.Join(", ", unused)}.";
            }

            return null;
        }

        /// <summary>
        /// 用样例值真正渲染一遍模板。占位符可能位于字符串外（如 <c>"seed": {{seed}}</c>），
        /// 因此不能在替换前直接解析 JSON；渲染后才能确认整份工作流是合法 JSON 对象。
        /// </summary>
        private static string? ValidateTemplateIsRenderable(CustomImageWorkflow workflow)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var parameter in workflow.Parameters)
            {
                object? value;

                if (!parameter.Required && !string.IsNullOrEmpty(parameter.DefaultValue))
                {
                    var (defaultValue, defaultError) =
                        ImageWorkflowParameterRules.ParseText(parameter, parameter.DefaultValue);
                    if (defaultError != null)
                        return $"Custom workflow '{workflow.Id}': {defaultError}";

                    value = defaultValue;
                }
                else
                {
                    value = parameter.Type switch
                    {
                        ImageWorkflowParameterType.Integer => 1L,
                        ImageWorkflowParameterType.Number => 1.0,
                        ImageWorkflowParameterType.Boolean => true,
                        ImageWorkflowParameterType.Enum => parameter.EnumValues[0],
                        _ => "value"
                    };
                }

                values[parameter.Name] = value!;
            }

            var (_, error) = WorkflowTemplateEngine.Render(workflow.WorkflowJson ?? "", values);
            return error == null
                ? null
                : $"Custom workflow '{workflow.Id}' template cannot be rendered: {error}";
        }
    }
}
