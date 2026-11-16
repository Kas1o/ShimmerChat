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

    /// <summary>文生图后端协议。新增协议时同步更新 <see cref="ImageApiProviderCatalog"/>。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ImageApiProvider
    {
        /// <summary>ComfyUI 的 /prompt → /history → /view 协议。</summary>
        ComfyUi = 0,

        /// <summary>Automatic1111 风格的 /sdapi/v1/txt2img 协议。</summary>
        Automatic1111 = 1
    }

    /// <summary>ComfyUI 配置的工作流来源。A1111 只有标准 txt2img，不支持自定义工作流。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ImageWorkflowMode
    {
        /// <summary>内置默认工作流，模型 / 宽高 / 采样参数可在工具调用中覆盖。</summary>
        Default = 0,

        /// <summary>用户提供的 API 格式 ComfyUI 工作流 JSON，参数由模板占位符声明。</summary>
        Custom = 1
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
    /// 旧版配置中的自定义 ComfyUI 工作流。仅用于迁移到 <see cref="ImageGenerationProfile"/>，
    /// 新代码不再直接读写它。
    /// </summary>
    public sealed class CustomImageWorkflow
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        public bool Enabled { get; set; } = true;

        public string WorkflowJson { get; set; } = "";

        public List<ImageWorkflowParameter> Parameters { get; set; } = [];
    }

    /// <summary>
    /// 默认工作流的可调参数。A1111 配置复用同一组值并映射到 txt2img 请求；
    /// 工具调用可以逐个覆盖，未覆盖时使用这里的值。
    /// </summary>
    public sealed class DefaultImageWorkflowSettings
    {
        /// <summary>
        /// 服务方的模型标识。ComfyUI 为 <c>CheckpointLoaderSimple.ckpt_name</c>，
        /// A1111 为 <c>sd_model_checkpoint</c>。允许留空（使用服务当前模型）。
        /// </summary>
        public string Model { get; set; } = "";

        public int Width { get; set; } = 512;

        public int Height { get; set; } = 512;

        public int Steps { get; set; } = 20;

        public double CfgScale { get; set; } = 7.0;

        /// <summary>采样器名。ComfyUI 用 <c>euler</c>，A1111 用 <c>Euler a</c> 这类名称。</summary>
        public string Sampler { get; set; } = "euler";

        /// <summary>仅 ComfyUI 使用；A1111 忽略此项。</summary>
        public string Scheduler { get; set; } = "normal";

        public string NegativePrompt { get; set; } = "";

        public int BatchSize { get; set; } = 1;
    }

    /// <summary>
    /// 一份完整的文生图配置：一个 API 端点 + 一种生成方式。
    /// 同一份全局配置里可以保存多份 <see cref="ImageGenerationProfile"/>，
    /// 节点通过 <see cref="Id"/> 选择要暴露给模型的那一份。
    /// </summary>
    public sealed class ImageGenerationProfile
    {
        /// <summary>持久化标识，也用于生成工具名：<c>generate_image_{Id}</c>。</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>给模型看的工具说明补充。</summary>
        public string Description { get; set; } = "";

        /// <summary>只有启用的配置才会被工具集节点暴露给模型；停用时允许是未完成的草稿。</summary>
        public bool Enabled { get; set; } = true;

        public ImageApiProvider Provider { get; set; } = ImageApiProvider.ComfyUi;

        /// <summary>API 根地址，例如 <c>http://127.0.0.1:8188</c> 或 <c>http://127.0.0.1:7860</c>。</summary>
        public string BaseUrl { get; set; } = "";

        /// <summary>
        /// 可选的 API 凭据。含冒号时按 HTTP Basic（<c>user:password</c>）发送，
        /// 否则按 Bearer Token 发送。留空表示不发送 Authorization 头。
        /// </summary>
        public string ApiKey { get; set; } = "";

        /// <summary>单次 HTTP 请求超时（秒）。</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>从提交任务到拿到结果的总体等待上限（秒）。</summary>
        public int MaxWaitSeconds { get; set; } = 300;

        /// <summary>轮询 ComfyUI /history 的间隔（毫秒）；A1111 同步请求不使用。</summary>
        public int PollIntervalMilliseconds { get; set; } = 1000;

        /// <summary>仅 ComfyUI 使用的工作流来源。</summary>
        public ImageWorkflowMode WorkflowMode { get; set; } = ImageWorkflowMode.Default;

        /// <summary>生成参数默认值，两种协议共用。</summary>
        public DefaultImageWorkflowSettings DefaultWorkflow { get; set; } = new();

        /// <summary>仅 <see cref="WorkflowMode"/> = <see cref="ImageWorkflowMode.Custom"/> 时使用。</summary>
        public string CustomWorkflowJson { get; set; } = "";

        /// <summary>自定义工作流的工具参数声明。</summary>
        public List<ImageWorkflowParameter> CustomWorkflowParameters { get; set; } = [];

        public static ImageGenerationProfile CreateDefault() => new()
        {
            Id = ImageGenerationConfigManager.DefaultProfileId,
            Name = "Default",
            Provider = ImageApiProvider.ComfyUi,
            BaseUrl = ImageApiProviderCatalog.Get(ImageApiProvider.ComfyUi).DefaultBaseUrl,
            WorkflowMode = ImageWorkflowMode.Default,
            DefaultWorkflow = new DefaultImageWorkflowSettings()
        };
    }

    /// <summary>文生图全局配置（KVData "ImageGeneration/Config"）。</summary>
    public sealed class ImageGenerationConfig
    {
        /// <summary>配置结构版本。低于 <see cref="ImageGenerationConfigManager.CurrentVersion"/> 时在读取阶段迁移。</summary>
        public int Version { get; set; }

        /// <summary>所有具名配置。节点通过 Id 选择其中一份，留空表示全部启用的配置。</summary>
        public List<ImageGenerationProfile> Profiles { get; set; } = [];

        // ---- 旧版（Version 0/1）字段，仅用于读取迁移 ----

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ComfyUiBaseUrl { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxWaitSeconds { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PollIntervalMilliseconds { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DefaultImageWorkflowSettings? DefaultWorkflow { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<CustomImageWorkflow>? CustomWorkflows { get; set; }

        /// <summary>查找配置；返回 null 表示没有配置该 Id。</summary>
        public ImageGenerationProfile? FindProfile(string id) =>
            Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

        /// <summary>首次使用 / 恢复默认值时的配置：一份可直接编辑的 ComfyUI 默认工作流。</summary>
        public static ImageGenerationConfig CreateDefault() => new()
        {
            Version = ImageGenerationConfigManager.CurrentVersion,
            Profiles = [ImageGenerationProfile.CreateDefault()]
        };
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

    /// <summary>ImageGenerationConfig 的 KVData 读写、版本迁移与集中校验。</summary>
    public static class ImageGenerationConfigManager
    {
        public const string SpaceId = "ImageGeneration";
        public const string ConfigKey = "Config";

        /// <summary>当前配置结构版本。版本 0/1 为旧版「默认工作流 + 自定义工作流列表」结构。</summary>
        public const int CurrentVersion = 2;

        /// <summary>旧版默认工作流迁移后使用的 Id，同时保留工具名 <c>generate_image</c>。</summary>
        public const string DefaultProfileId = "default";

        /// <summary>工具名前缀。Id 为 <see cref="DefaultProfileId"/> 时直接使用该名字。</summary>
        public const string DefaultToolName = "generate_image";

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

        /// <summary>配置 Id：直接拼进工具名，因此限定为安全字符。</summary>
        public static readonly Regex ProfileIdPattern = new(
            "^[A-Za-z0-9_-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>配置 Id 对应的工具名；唯一入口，选项提供者与工具都读它。</summary>
        public static string GetToolName(string profileId) =>
            string.Equals(profileId, DefaultProfileId, StringComparison.Ordinal)
                ? DefaultToolName
                : $"{DefaultToolName}_{profileId}";

        /// <summary>
        /// 读取配置。未写入过时返回一份可直接使用的默认配置；JSON 损坏或取值非法时返回错误说明，
        /// 由调用方决定如何汇报（不静默回落成默认值）。
        /// </summary>
        public static (ImageGenerationConfig? Config, string? Error) Load(IKVDataService kvData)
        {
            var json = kvData.Read(SpaceId, ConfigKey);
            if (string.IsNullOrWhiteSpace(json))
                return (ImageGenerationConfig.CreateDefault(), null);

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

            config = Upgrade(config);
            var error = Validate(config);
            return error == null ? (config, null) : (null, error);
        }

        /// <summary>
        /// 把旧版结构迁移成配置文件列表。迁移只在内存中完成，下一次保存时写成新结构。
        /// 该函数必须幂等：新结构原样返回。
        /// </summary>
        public static ImageGenerationConfig Upgrade(ImageGenerationConfig config)
        {
            if (config.Version >= CurrentVersion)
                return config;

            config.Profiles ??= [];

            if (config.Profiles.Count == 0)
            {
                var customWorkflows = config.CustomWorkflows ?? [];
                if (config.DefaultWorkflow != null || customWorkflows.Count > 0)
                {
                    var customIds = customWorkflows
                        .Select(w => w.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToHashSet(StringComparer.Ordinal);

                    // 旧格式没有保留 Id，迁移时把默认工作流命名为 default 以保留工具名。
                    // 极端情况下用户的自定义工作流就叫 default，则让默认配置退让。
                    var defaultId = customIds.Contains(DefaultProfileId)
                        ? $"{DefaultProfileId}_comfy"
                        : DefaultProfileId;

                    var baseUrl = string.IsNullOrWhiteSpace(config.ComfyUiBaseUrl)
                        ? ImageApiProviderCatalog.Get(ImageApiProvider.ComfyUi).DefaultBaseUrl
                        : config.ComfyUiBaseUrl;

                    var settings = config.DefaultWorkflow ?? new DefaultImageWorkflowSettings();
                    var migrated = new List<ImageGenerationProfile>();
                    var migratedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (config.DefaultWorkflow != null)
                    {
                        migrated.Add(new ImageGenerationProfile
                        {
                            Id = defaultId,
                            Name = UniqueName("Default", migratedNames),
                            Description = "",
                            Enabled = true,
                            Provider = ImageApiProvider.ComfyUi,
                            BaseUrl = baseUrl,
                            TimeoutSeconds = config.TimeoutSeconds ?? 30,
                            MaxWaitSeconds = config.MaxWaitSeconds ?? 300,
                            PollIntervalMilliseconds = config.PollIntervalMilliseconds ?? 1000,
                            WorkflowMode = ImageWorkflowMode.Default,
                            DefaultWorkflow = settings
                        });
                    }

                    foreach (var workflow in customWorkflows)
                    {
                        if (workflow == null)
                            continue;

                        var name = string.IsNullOrWhiteSpace(workflow.Name) ? workflow.Id : workflow.Name;

                        migrated.Add(new ImageGenerationProfile
                        {
                            Id = workflow.Id,
                            Name = UniqueName(name, migratedNames),
                            Description = workflow.Description ?? "",
                            Enabled = workflow.Enabled,
                            Provider = ImageApiProvider.ComfyUi,
                            BaseUrl = baseUrl,
                            TimeoutSeconds = config.TimeoutSeconds ?? 30,
                            MaxWaitSeconds = config.MaxWaitSeconds ?? 300,
                            PollIntervalMilliseconds = config.PollIntervalMilliseconds ?? 1000,
                            WorkflowMode = ImageWorkflowMode.Custom,
                            DefaultWorkflow = settings,
                            CustomWorkflowJson = workflow.WorkflowJson ?? "",
                            CustomWorkflowParameters = workflow.Parameters ?? []
                        });
                    }

                    config.Profiles = migrated;
                }
            }

            config.Version = CurrentVersion;
            config.ComfyUiBaseUrl = null;
            config.TimeoutSeconds = null;
            config.MaxWaitSeconds = null;
            config.PollIntervalMilliseconds = null;
            config.DefaultWorkflow = null;
            config.CustomWorkflows = null;

            return config;
        }

        private static string UniqueName(string baseName, HashSet<string> used)
        {
            var name = string.IsNullOrWhiteSpace(baseName) ? "Configuration" : baseName;
            var candidate = name;
            var suffix = 2;

            while (!used.Add(candidate))
                candidate = $"{name} ({suffix++})";

            return candidate;
        }

        /// <summary>
        /// 校验配置取值。设置面板保存与 KVData 读取共用同一判定，避免两边规则不一致。
        /// 返回错误说明，null 表示合法。
        /// </summary>
        public static string? Validate(ImageGenerationConfig config)
        {
            if (config.Profiles == null)
                return "Profiles must be a list.";

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in config.Profiles)
            {
                var error = ValidateProfile(profile, ids, names);
                if (error != null)
                    return error;
            }

            return null;
        }

        public static void Save(IKVDataService kvData, ImageGenerationConfig config)
        {
            config.Version = CurrentVersion;
            kvData.Write(SpaceId, ConfigKey, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        private static string? ValidateProfile(
            ImageGenerationProfile profile, HashSet<string> ids, HashSet<string> names)
        {
            if (profile == null)
                return "Profiles contains a null entry.";

            if (profile.Id == null || !ProfileIdPattern.IsMatch(profile.Id))
            {
                return $"Image configuration id '{profile.Id}' is invalid. " +
                       "Use 1-40 characters: letters, digits, underscore or hyphen.";
            }

            if (!ids.Add(profile.Id))
                return $"Duplicate image configuration id '{profile.Id}'.";

            if (string.IsNullOrWhiteSpace(profile.Name))
                return $"Image configuration '{profile.Id}' must have a name.";

            if (!names.Add(profile.Name))
                return $"Duplicate image configuration name '{profile.Name}'.";

            if (!Enum.IsDefined(profile.Provider))
                return $"Image configuration '{profile.Id}' has unsupported provider {profile.Provider}.";

            var descriptor = ImageApiProviderCatalog.Get(profile.Provider);

            var baseUrl = profile.BaseUrl?.Trim();
            if (string.IsNullOrEmpty(baseUrl))
                return $"Image configuration '{profile.Id}' BaseUrl must not be empty.";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return $"Image configuration '{profile.Id}' BaseUrl must be an absolute http(s) URL, " +
                       $"got '{profile.BaseUrl}'.";
            }

            if (profile.ApiKey == null)
                return $"Image configuration '{profile.Id}' ApiKey must not be null.";

            if (profile.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                return $"Image configuration '{profile.Id}' TimeoutSeconds must be between " +
                       $"{MinTimeoutSeconds} and {MaxTimeoutSeconds}, got {profile.TimeoutSeconds}.";

            if (profile.MaxWaitSeconds is < MinMaxWaitSeconds or > MaxMaxWaitSeconds)
                return $"Image configuration '{profile.Id}' MaxWaitSeconds must be between " +
                       $"{MinMaxWaitSeconds} and {MaxMaxWaitSeconds}, got {profile.MaxWaitSeconds}.";

            if (profile.PollIntervalMilliseconds is < MinPollIntervalMilliseconds or > MaxPollIntervalMilliseconds)
            {
                return $"Image configuration '{profile.Id}' PollIntervalMilliseconds must be between " +
                       $"{MinPollIntervalMilliseconds} and {MaxPollIntervalMilliseconds}, " +
                       $"got {profile.PollIntervalMilliseconds}.";
            }

            if (profile.DefaultWorkflow == null)
                return $"Image configuration '{profile.Id}' DefaultWorkflow must not be null.";

            var settingsError = ValidateGenerationSettings(profile, descriptor);
            if (settingsError != null)
                return settingsError;

            if (!Enum.IsDefined(profile.WorkflowMode))
                return $"Image configuration '{profile.Id}' has unsupported workflow mode {profile.WorkflowMode}.";

            if (!descriptor.SupportsCustomWorkflow && profile.WorkflowMode != ImageWorkflowMode.Default)
            {
                return $"Image configuration '{profile.Id}' uses {descriptor.DisplayName}, " +
                       "which does not support custom workflows.";
            }

            if (profile.WorkflowMode == ImageWorkflowMode.Custom && profile.Enabled)
            {
                var customError = ValidateCustomWorkflow(profile);
                if (customError != null)
                    return customError;
            }

            return null;
        }

        private static string? ValidateGenerationSettings(
            ImageGenerationProfile profile, ImageApiProviderDescriptor descriptor)
        {
            var id = profile.Id;
            var settings = profile.DefaultWorkflow;

            if (settings.Width is < MinDimension or > MaxDimension)
                return $"Image configuration '{id}' Width must be between {MinDimension} and {MaxDimension}, got {settings.Width}.";

            if (settings.Width % 8 != 0)
                return $"Image configuration '{id}' Width must be a multiple of 8, got {settings.Width}.";

            if (settings.Height is < MinDimension or > MaxDimension)
                return $"Image configuration '{id}' Height must be between {MinDimension} and {MaxDimension}, got {settings.Height}.";

            if (settings.Height % 8 != 0)
                return $"Image configuration '{id}' Height must be a multiple of 8, got {settings.Height}.";

            if (settings.Steps is < MinSteps or > MaxSteps)
                return $"Image configuration '{id}' Steps must be between {MinSteps} and {MaxSteps}, got {settings.Steps}.";

            if (!double.IsFinite(settings.CfgScale) || settings.CfgScale is < MinCfgScale or > MaxCfgScale)
                return $"Image configuration '{id}' CfgScale must be between {MinCfgScale} and {MaxCfgScale}, got {settings.CfgScale}.";

            if (settings.BatchSize is < MinBatchSize or > MaxBatchSize)
                return $"Image configuration '{id}' BatchSize must be between {MinBatchSize} and {MaxBatchSize}, got {settings.BatchSize}.";

            if (string.IsNullOrWhiteSpace(settings.Sampler))
                return $"Image configuration '{id}' Sampler must not be empty.";

            if (descriptor.SupportsScheduler && string.IsNullOrWhiteSpace(settings.Scheduler))
                return $"Image configuration '{id}' Scheduler must not be empty.";

            if (settings.NegativePrompt == null)
                return $"Image configuration '{id}' NegativePrompt must not be null.";

            // Model 允许为空：用户可能还没启动服务或安装 checkpoint。
            // 执行默认工作流时由工具 / 工具集节点给出明确失败，而不是在这里阻止保存其他设置。
            return null;
        }

        private static string? ValidateCustomWorkflow(ImageGenerationProfile profile)
        {
            if (profile.CustomWorkflowParameters == null)
                return $"Image configuration '{profile.Id}' CustomWorkflowParameters must be a list.";

            var (foundPlaceholders, placeholderError) =
                WorkflowTemplateEngine.FindPlaceholders(profile.CustomWorkflowJson ?? "");
            if (placeholderError != null)
                return $"Image configuration '{profile.Id}' template is invalid: {placeholderError}";

            var placeholders = foundPlaceholders!;
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in profile.CustomWorkflowParameters)
            {
                if (parameter == null)
                    return $"Image configuration '{profile.Id}' contains a null parameter.";

                if (!parameterNames.Add(parameter.Name))
                    return $"Image configuration '{profile.Id}' declares duplicate parameter '{parameter.Name}'.";

                var definitionError = ImageWorkflowParameterRules.ValidateDefinition(parameter);
                if (definitionError != null)
                    return $"Image configuration '{profile.Id}': {definitionError}";
            }

            var undeclared = placeholders.Where(p => !parameterNames.Contains(p)).ToList();
            if (undeclared.Count > 0)
            {
                return $"Image configuration '{profile.Id}' uses undeclared placeholder(s): " +
                       $"{string.Join(", ", undeclared)}.";
            }

            var renderError = ValidateTemplateIsRenderable(profile);
            if (renderError != null)
                return renderError;

            var unused = parameterNames.Where(p => !placeholders.Contains(p)).ToList();
            if (unused.Count > 0)
            {
                return $"Image configuration '{profile.Id}' declares parameter(s) that never appear " +
                       $"in the workflow template: {string.Join(", ", unused)}.";
            }

            return null;
        }

        /// <summary>
        /// 用样例值真正渲染一遍模板。占位符可能位于字符串外（如 <c>"seed": {{seed}}</c>），
        /// 因此不能在替换前直接解析 JSON；渲染后才能确认整份工作流是合法 JSON 对象。
        /// </summary>
        private static string? ValidateTemplateIsRenderable(ImageGenerationProfile profile)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var parameter in profile.CustomWorkflowParameters)
            {
                object? value;

                if (!parameter.Required && !string.IsNullOrEmpty(parameter.DefaultValue))
                {
                    var (defaultValue, defaultError) =
                        ImageWorkflowParameterRules.ParseText(parameter, parameter.DefaultValue);
                    if (defaultError != null)
                        return $"Image configuration '{profile.Id}': {defaultError}";

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

            var (_, error) = WorkflowTemplateEngine.Render(profile.CustomWorkflowJson ?? "", values);
            return error == null
                ? null
                : $"Image configuration '{profile.Id}' template cannot be rendered: {error}";
        }
    }
}
