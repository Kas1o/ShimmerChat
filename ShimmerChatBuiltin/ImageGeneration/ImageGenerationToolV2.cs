using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 文生图工具。一份 <see cref="ImageGenerationProfile"/> 对应一个工具：
    /// <list type="bullet">
    /// <item>ComfyUI 默认工作流：模型、宽高、采样参数可由工具参数覆盖；</item>
    /// <item>ComfyUI 自定义工作流：工具形参完全来自配置中的参数声明，工作流 JSON 由模板引擎替换；</item>
    /// <item>A1111：标准 txt2img 参数，模型 / 宽高 / 采样参数可由工具参数覆盖。</item>
    /// </list>
    /// 生成结果经 <see cref="IImageAttachmentService.SaveGeneratedAsync"/> 落到 UserUploadImage，
    /// 返回内容里带上 <c>/userimages/...</c> 的 Markdown 链接，供模型直接嵌入回复。
    /// </summary>
    public sealed class ImageGenerationToolV2 : IAutoCreateToolV2, IToolV3
    {
        public const string DefaultToolName = ImageGenerationConfigManager.DefaultToolName;

        public static string NameKey => "tool.generate_image";
        public static string DescriptionKey => "tool.generate_image.desc";
        public static string[] CategoryKeys => ["category.image_generation", "category.content"];

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new ImageGenerationToolV2(env.KVData, env.ImageAttachments, profileId: null);

        private static readonly HashSet<string> ComfyArgumentNames = new(StringComparer.Ordinal)
        {
            "prompt",
            "negative_prompt",
            "model",
            "width",
            "height",
            "steps",
            "cfg",
            "sampler",
            "scheduler",
            "seed",
            "batch_size"
        };

        private static readonly HashSet<string> A1111ArgumentNames = new(StringComparer.Ordinal)
        {
            "prompt",
            "negative_prompt",
            "model",
            "width",
            "height",
            "steps",
            "cfg",
            "sampler",
            "seed",
            "batch_size"
        };

        private readonly IImageAttachmentService? _imageAttachments;
        private readonly string? _profileId;
        private readonly Func<ImageGenerationProfile, IComfyUiClient> _comfyClientFactory;
        private readonly Func<ImageGenerationProfile, IA1111Client> _a1111ClientFactory;
        private readonly ImageGenerationConfig? _config;
        private readonly string? _configError;

        public ImageGenerationToolV2(
            IKVDataService kvData,
            IImageAttachmentService? imageAttachments,
            string? profileId)
            : this(kvData, imageAttachments, profileId, comfyClientFactory: null, a1111ClientFactory: null)
        {
        }

        internal ImageGenerationToolV2(
            IKVDataService kvData,
            IImageAttachmentService? imageAttachments,
            string? profileId,
            Func<ImageGenerationProfile, IComfyUiClient>? comfyClientFactory,
            Func<ImageGenerationProfile, IA1111Client>? a1111ClientFactory = null)
        {
            _imageAttachments = imageAttachments;
            _profileId = profileId;
            _comfyClientFactory = comfyClientFactory ?? CreateComfyClient;
            _a1111ClientFactory = a1111ClientFactory ?? CreateA1111Client;

            (_config, _configError) = ImageGenerationConfigManager.Load(kvData);
        }

        public Tool GetDefinition()
        {
            var profile = ResolveProfile();

            if (profile == null)
            {
                return new Tool
                {
                    name = ResolveToolName(),
                    description = _profileId == null
                        ? "Image generation is not configured correctly. " +
                          "Ask the user to check the Image Generation settings panel."
                        : $"Image generation configuration '{_profileId}' is not configured or is disabled. " +
                          "Ask the user to check the Image Generation settings panel.",
                    parameters = []
                };
            }

            var descriptor = ImageApiProviderCatalog.Get(profile.Provider);

            if (profile.Provider == ImageApiProvider.ComfyUi
                && profile.WorkflowMode == ImageWorkflowMode.Custom)
            {
                return new Tool
                {
                    name = ImageGenerationConfigManager.GetToolName(profile.Id),
                    description = BuildCustomDescription(profile),
                    parameters = BuildCustomParameters(profile.CustomWorkflowParameters ?? [])
                };
            }

            return new Tool
            {
                name = ImageGenerationConfigManager.GetToolName(profile.Id),
                description = BuildStandardDescription(profile, descriptor),
                parameters = BuildStandardParameters(profile, descriptor)
            };
        }

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            if (_config == null)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    "Image generation is not configured correctly. Check the Image Generation settings panel.",
                    _configError);
            }

            var profile = ResolveProfile();
            if (profile == null)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    _profileId == null
                        ? "No enabled image generation configuration is available."
                        : $"Image generation configuration '{_profileId}' is not configured.");
            }

            if (!profile.Enabled)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    $"Image generation configuration '{profile.Name}' is disabled in the settings.");
            }

            if (_imageAttachments == null)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    "Image storage is unavailable in the current session, so generated images cannot be saved.");
            }

            var arguments = context.TryGetArgumentsObject();
            if (arguments == null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "Arguments must be a JSON object.");

            var (images, label, seed, errorCode, error, details) =
                await GenerateImagesAsync(profile, arguments, ct);

            if (images == null)
                return ToolResult.Fail(errorCode!, error!, details);

            var links = new List<string>(images.Count);
            var savedImages = new List<MessageImage>(images.Count);

            // 每次执行时折算输出处理：全局设置热切换后立即生效，不缓存到工具实例里。
            var processing = profile.OutputImageProcessing.ResolveOutput(_imageAttachments.UploadProcessing);

            foreach (var image in images)
            {
                MessageImage saved;
                try
                {
                    using var stream = new MemoryStream(image.Content, writable: false);
                    saved = await _imageAttachments.SaveGeneratedAsync(stream, image.ContentType, processing, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return ToolResult.Fail(
                        ToolErrorCodes.OutputWriteFailed,
                        $"Failed to save generated image '{image.FileName}'.",
                        ex.ToString());
                }

                var url = _imageAttachments.GetDisplayUrl(saved);
                if (string.IsNullOrWhiteSpace(url))
                {
                    return ToolResult.Fail(
                        ToolErrorCodes.OutputWriteFailed,
                        "The generated image was saved but no display URL could be resolved.",
                        $"FileName={saved.FileName}, MimeType={saved.MimeType}");
                }

                links.Add(url);
                savedImages.Add(saved);
            }

            // 链接给"回复里展示"，图像附件给模型"下一轮真正看到"；两者缺一不可。
            return ToolResult.Ok(FormatResult(links, label, seed), savedImages);
        }

        private async Task<(List<GeneratedImage>? Images, string Label, long? Seed,
            string? ErrorCode, string? Error, string? Details)> GenerateImagesAsync(
            ImageGenerationProfile profile, JObject arguments, CancellationToken ct)
        {
            var label = profile.Name;

            if (profile.Provider == ImageApiProvider.Automatic1111)
            {
                var descriptor = ImageApiProviderCatalog.Get(profile.Provider);
                var (request, argumentError) =
                    ResolveStandardArguments(arguments, profile, descriptor);

                if (request == null)
                    return (null, label, null, ToolErrorCodes.InvalidArguments, argumentError, null);

                var client = _a1111ClientFactory(profile);
                var generation = await client.GenerateAsync(
                    new A1111Txt2ImgRequest(
                        request.Prompt,
                        request.NegativePrompt,
                        request.Model,
                        request.Sampler,
                        request.Width,
                        request.Height,
                        request.Steps,
                        request.CfgScale,
                        request.Seed,
                        request.BatchSize),
                    ct);

                if (!generation.Success)
                {
                    return (null, label, null, ToolErrorCodes.ServiceError,
                        $"{descriptor.DisplayName} image generation failed: {generation.Error}",
                        generation.Details);
                }

                return (
                    generation.Images
                        .Select(i => new GeneratedImage(i.FileName, i.ContentType, i.Content))
                        .ToList(),
                    label,
                    request.Seed,
                    null,
                    null,
                    null);
            }

            var comfyDescriptor = ImageApiProviderCatalog.Get(ImageApiProvider.ComfyUi);
            JObject workflow;
            long? seed;

            if (profile.WorkflowMode == ImageWorkflowMode.Custom)
            {
                var (values, bindError) =
                    WorkflowTemplateEngine.Bind(profile.CustomWorkflowParameters ?? [], arguments);
                if (values == null)
                    return (null, label, null, ToolErrorCodes.InvalidArguments, bindError, null);

                var (rendered, renderError) =
                    WorkflowTemplateEngine.Render(profile.CustomWorkflowJson, values);
                if (rendered == null)
                {
                    return (null, label, null, ToolErrorCodes.ConfigInvalid,
                        $"Image generation configuration '{profile.Name}' could not be rendered.",
                        renderError);
                }

                workflow = rendered;
                seed = null;
            }
            else
            {
                var (request, argumentError) =
                    ResolveStandardArguments(arguments, profile, comfyDescriptor);

                if (request == null)
                    return (null, label, null, ToolErrorCodes.InvalidArguments, argumentError, null);

                workflow = DefaultImageWorkflow.Build(
                    request.Model,
                    request.Prompt,
                    request.NegativePrompt,
                    request.Width,
                    request.Height,
                    request.Steps,
                    request.CfgScale,
                    request.Sampler,
                    request.Scheduler,
                    request.Seed,
                    request.BatchSize);

                seed = request.Seed;
            }

            var comfyClient = _comfyClientFactory(profile);
            var comfyGeneration = await comfyClient.GenerateAsync(workflow, ct);

            if (!comfyGeneration.Success)
            {
                return (null, label, null, ToolErrorCodes.ServiceError,
                    $"{comfyDescriptor.DisplayName} image generation failed: {comfyGeneration.Error}",
                    comfyGeneration.Details);
            }

            return (
                comfyGeneration.Images
                    .Select(i => new GeneratedImage(i.FileName, i.ContentType, i.Content))
                    .ToList(),
                label,
                seed,
                null,
                null,
                null);
        }

        private ImageGenerationProfile? ResolveProfile()
        {
            if (_config == null)
                return null;

            if (!string.IsNullOrWhiteSpace(_profileId))
                return _config.FindProfile(_profileId);

            // 自动创建的工具（没有指定 Id）优先使用默认配置，否则使用第一份启用的配置。
            var defaultProfile = _config.FindProfile(ImageGenerationConfigManager.DefaultProfileId);
            if (defaultProfile != null && defaultProfile.Enabled)
                return defaultProfile;

            return _config.Profiles.FirstOrDefault(p => p.Enabled);
        }

        private string ResolveToolName() =>
            string.IsNullOrWhiteSpace(_profileId)
                ? DefaultToolName
                : ImageGenerationConfigManager.GetToolName(_profileId!);

        private static string BuildStandardDescription(
            ImageGenerationProfile profile, ImageApiProviderDescriptor descriptor)
        {
            var model = string.IsNullOrWhiteSpace(profile.DefaultWorkflow.Model)
                ? "not configured (uses the server's current model)"
                : profile.DefaultWorkflow.Model;

            var parameters = descriptor.SupportsScheduler
                ? "Width, height, steps, CFG, sampler, scheduler, seed and batch size"
                : "Width, height, steps, CFG, sampler, seed and batch size";

            var description =
                $"Generate an image from a text prompt with {descriptor.DisplayName} " +
                $"using the image configuration '{profile.Name}'. Configured model: {model}. " +
                $"{parameters} can be overridden per call.";

            if (!string.IsNullOrWhiteSpace(profile.Description))
                description += " " + profile.Description.Trim();

            return description +
                   " On success the tool returns /userimages/... links; embed the returned Markdown image " +
                   "snippet in your reply so the user can see the image.";
        }

        private static string BuildCustomDescription(ImageGenerationProfile profile)
        {
            var description =
                $"Generate an image with the ComfyUI custom workflow '{profile.Name}'.";

            if (!string.IsNullOrWhiteSpace(profile.Description))
                description += " " + profile.Description.Trim();

            return description +
                   " On success the tool returns /userimages/... links; embed the returned Markdown image " +
                   "snippet in your reply so the user can see the image.";
        }

        private static List<(ToolParameter Parameter, bool Required)> BuildStandardParameters(
            ImageGenerationProfile profile, ImageApiProviderDescriptor descriptor)
        {
            var settings = profile.DefaultWorkflow;
            var parameters = new List<(ToolParameter, bool)>
            {
                (Param("prompt", ParameterType.String,
                    "Text prompt describing the image to generate."), true),

                (Param("negative_prompt", ParameterType.String,
                    "Things to avoid in the image. Defaults to the configured negative prompt" +
                    (string.IsNullOrWhiteSpace(settings.NegativePrompt)
                        ? " (currently empty)."
                        : $": {settings.NegativePrompt}")), false),

                (Param("model", ParameterType.String,
                    descriptor.Kind == ImageApiProvider.ComfyUi
                        ? (string.IsNullOrWhiteSpace(settings.Model)
                            ? "Checkpoint name (ckpt_name) to load. Required: no default model is configured."
                            : $"Checkpoint name (ckpt_name) to load. Defaults to '{settings.Model}'.")
                        : (string.IsNullOrWhiteSpace(settings.Model)
                            ? "Checkpoint name to load (sd_model_checkpoint). Defaults to the server's current model."
                            : $"Checkpoint name to load (sd_model_checkpoint). Defaults to '{settings.Model}'.")),
                    false),

                (Param("width", ParameterType.Number,
                    $"Image width in pixels (multiple of 8, {ImageGenerationConfigManager.MinDimension}-" +
                    $"{ImageGenerationConfigManager.MaxDimension}). Defaults to {settings.Width}."), false),

                (Param("height", ParameterType.Number,
                    $"Image height in pixels (multiple of 8, {ImageGenerationConfigManager.MinDimension}-" +
                    $"{ImageGenerationConfigManager.MaxDimension}). Defaults to {settings.Height}."), false),

                (Param("steps", ParameterType.Number,
                    $"Number of sampling steps ({ImageGenerationConfigManager.MinSteps}-" +
                    $"{ImageGenerationConfigManager.MaxSteps}). Defaults to {settings.Steps}."), false),

                (Param("cfg", ParameterType.Number,
                    $"CFG scale ({ImageGenerationConfigManager.MinCfgScale}-" +
                    $"{ImageGenerationConfigManager.MaxCfgScale.ToString(CultureInfo.InvariantCulture)}). " +
                    $"Defaults to {settings.CfgScale.ToString(CultureInfo.InvariantCulture)}."), false),

                (Param("sampler", ParameterType.String,
                    $"Sampler name, e.g. euler / dpmpp_2m. Defaults to '{settings.Sampler}'."), false),

                (Param("seed", ParameterType.Number,
                    "Random seed. Omit it to pick a random seed for each call."), false),

                (Param("batch_size", ParameterType.Number,
                    $"Number of images to generate in one batch ({ImageGenerationConfigManager.MinBatchSize}-" +
                    $"{ImageGenerationConfigManager.MaxBatchSize}). Defaults to {settings.BatchSize}."), false)
            };

            if (descriptor.SupportsScheduler)
            {
                parameters.Insert(7, (Param("scheduler", ParameterType.String,
                    $"Scheduler name, e.g. normal / karras. Defaults to '{settings.Scheduler}'."), false));
            }

            return parameters;
        }

        private static List<(ToolParameter Parameter, bool Required)> BuildCustomParameters(
            IReadOnlyList<ImageWorkflowParameter> parameters)
        {
            var result = new List<(ToolParameter, bool)>(parameters.Count);
            foreach (var parameter in parameters)
            {
                var description = parameter.Description?.Trim() ?? "";

                if (parameter.Type == ImageWorkflowParameterType.Enum)
                {
                    var values = string.Join(", ", parameter.EnumValues);
                    description = AppendSentence(description, $"One of: {values}.");
                }

                if (!parameter.Required)
                    description = AppendSentence(description, $"Default: '{parameter.DefaultValue}'.");

                var toolParameter = new ToolParameter
                {
                    name = parameter.Name,
                    description = description,
                    type = parameter.Type switch
                    {
                        ImageWorkflowParameterType.Integer or ImageWorkflowParameterType.Number =>
                            ParameterType.Number,
                        ImageWorkflowParameterType.Boolean => ParameterType.Boolean,
                        _ => ParameterType.String
                    }
                };

                if (parameter.Type == ImageWorkflowParameterType.Enum)
                    toolParameter.@enum = [.. parameter.EnumValues];

                result.Add((toolParameter, parameter.Required));
            }

            return result;
        }

        private static ToolParameter Param(string name, ParameterType type, string description) => new()
        {
            name = name,
            type = type,
            description = description
        };

        private static string AppendSentence(string text, string sentence) =>
            string.IsNullOrWhiteSpace(text) ? sentence : $"{text} {sentence}";

        private static (StandardArguments? Request, string? Error) ResolveStandardArguments(
            JObject arguments, ImageGenerationProfile profile, ImageApiProviderDescriptor descriptor)
        {
            var supportedNames = descriptor.SupportsScheduler
                ? ComfyArgumentNames
                : A1111ArgumentNames;

            var unknown = arguments.Properties()
                .Where(p => !supportedNames.Contains(p.Name))
                .Select(p => p.Name)
                .ToList();

            if (unknown.Count > 0)
            {
                return (null,
                    $"Unknown parameter(s): {string.Join(", ", unknown)}. " +
                    $"Supported parameters: {string.Join(", ", supportedNames)}.");
            }

            var settings = profile.DefaultWorkflow;

            var prompt = ReadString(arguments, "prompt", out var error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(prompt))
                return (null, "'prompt' is required and must be a non-empty string.");

            var negativePrompt = ReadString(arguments, "negative_prompt", out error);
            if (error != null) return (null, error);
            negativePrompt ??= settings.NegativePrompt;

            var model = ReadString(arguments, "model", out error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(model))
                model = settings.Model;

            if (descriptor.Kind == ImageApiProvider.ComfyUi && string.IsNullOrWhiteSpace(model))
            {
                return (null,
                    "No checkpoint model is configured for this ComfyUI image configuration. " +
                    "Ask the user to select one in Settings → Image Generation.");
            }

            var width = ReadInt(arguments, "width", out error);
            if (error != null) return (null, error);
            width ??= settings.Width;

            var height = ReadInt(arguments, "height", out error);
            if (error != null) return (null, error);
            height ??= settings.Height;

            var steps = ReadInt(arguments, "steps", out error);
            if (error != null) return (null, error);
            steps ??= settings.Steps;

            var cfg = ReadDouble(arguments, "cfg", out error);
            if (error != null) return (null, error);
            cfg ??= settings.CfgScale;

            var sampler = ReadString(arguments, "sampler", out error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(sampler))
                sampler = settings.Sampler;

            var scheduler = "";
            if (descriptor.SupportsScheduler)
            {
                var schedulerArgument = ReadString(arguments, "scheduler", out error);
                if (error != null) return (null, error);
                scheduler = string.IsNullOrWhiteSpace(schedulerArgument)
                    ? settings.Scheduler
                    : schedulerArgument;
            }

            var seed = ReadLong(arguments, "seed", out error);
            if (error != null) return (null, error);
            seed ??= Random.Shared.NextInt64(0, long.MaxValue);

            var batchSize = ReadInt(arguments, "batch_size", out error);
            if (error != null) return (null, error);
            batchSize ??= settings.BatchSize;

            if (width is < ImageGenerationConfigManager.MinDimension or > ImageGenerationConfigManager.MaxDimension)
            {
                return (null,
                    $"'width' must be between {ImageGenerationConfigManager.MinDimension} and " +
                    $"{ImageGenerationConfigManager.MaxDimension}; got {width}.");
            }

            if (width.Value % 8 != 0)
                return (null, $"'width' must be a multiple of 8; got {width}.");

            if (height is < ImageGenerationConfigManager.MinDimension or > ImageGenerationConfigManager.MaxDimension)
            {
                return (null,
                    $"'height' must be between {ImageGenerationConfigManager.MinDimension} and " +
                    $"{ImageGenerationConfigManager.MaxDimension}; got {height}.");
            }

            if (height.Value % 8 != 0)
                return (null, $"'height' must be a multiple of 8; got {height}.");

            if (steps is < ImageGenerationConfigManager.MinSteps or > ImageGenerationConfigManager.MaxSteps)
            {
                return (null,
                    $"'steps' must be between {ImageGenerationConfigManager.MinSteps} and " +
                    $"{ImageGenerationConfigManager.MaxSteps}; got {steps}.");
            }

            if (!double.IsFinite(cfg.Value)
                || cfg is < ImageGenerationConfigManager.MinCfgScale or > ImageGenerationConfigManager.MaxCfgScale)
            {
                return (null,
                    $"'cfg' must be between {ImageGenerationConfigManager.MinCfgScale} and " +
                    $"{ImageGenerationConfigManager.MaxCfgScale}; got {cfg}.");
            }

            if (batchSize is < ImageGenerationConfigManager.MinBatchSize or > ImageGenerationConfigManager.MaxBatchSize)
            {
                return (null,
                    $"'batch_size' must be between {ImageGenerationConfigManager.MinBatchSize} and " +
                    $"{ImageGenerationConfigManager.MaxBatchSize}; got {batchSize}.");
            }

            if (seed.Value < 0)
                return (null, $"'seed' must be 0 or greater; got {seed}.");

            return (new StandardArguments(
                prompt!,
                negativePrompt!,
                model!,
                width.Value,
                height.Value,
                steps.Value,
                cfg.Value,
                sampler!,
                scheduler,
                seed.Value,
                batchSize.Value), null);
        }

        private static string? ReadString(JObject arguments, string name, out string? error)
        {
            error = null;
            var token = arguments[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type != JTokenType.String)
            {
                error = $"'{name}' must be a string; got {token.Type}.";
                return null;
            }

            return token.Value<string>();
        }

        private static int? ReadInt(JObject arguments, string name, out string? error)
        {
            error = null;
            var token = arguments[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;

            long value;
            switch (token.Type)
            {
                case JTokenType.Integer:
                    value = token.Value<long>();
                    break;

                case JTokenType.Float:
                {
                    var number = token.Value<double>();
                    if (!double.IsFinite(number) || Math.Floor(number) != number)
                    {
                        error = $"'{name}' must be an integer; got {number.ToString(CultureInfo.InvariantCulture)}.";
                        return null;
                    }

                    if (number is < int.MinValue or > int.MaxValue)
                    {
                        error = $"'{name}' is outside the supported integer range.";
                        return null;
                    }

                    value = (long)number;
                    break;
                }

                case JTokenType.String:
                    if (!long.TryParse(token.Value<string>(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out value))
                    {
                        error = $"'{name}' must be an integer; got '{token.Value<string>()}'.";
                        return null;
                    }

                    break;

                default:
                    error = $"'{name}' must be a number; got {token.Type}.";
                    return null;
            }

            if (value is < int.MinValue or > int.MaxValue)
            {
                error = $"'{name}' is outside the supported integer range.";
                return null;
            }

            return (int)value;
        }

        private static double? ReadDouble(JObject arguments, string name, out string? error)
        {
            error = null;
            var token = arguments[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;

            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();

                case JTokenType.Float:
                {
                    var number = token.Value<double>();
                    if (!double.IsFinite(number))
                    {
                        error = $"'{name}' must be a finite number.";
                        return null;
                    }

                    return number;
                }

                case JTokenType.String:
                    if (!double.TryParse(token.Value<string>(), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var parsed)
                        || !double.IsFinite(parsed))
                    {
                        error = $"'{name}' must be a number; got '{token.Value<string>()}'.";
                        return null;
                    }

                    return parsed;

                default:
                    error = $"'{name}' must be a number; got {token.Type}.";
                    return null;
            }
        }

        private static long? ReadLong(JObject arguments, string name, out string? error)
        {
            error = null;
            var token = arguments[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;

            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();

                case JTokenType.Float:
                {
                    var number = token.Value<double>();
                    if (!double.IsFinite(number) || Math.Floor(number) != number
                        || number < long.MinValue || number > long.MaxValue)
                    {
                        error = $"'{name}' must be an integer.";
                        return null;
                    }

                    return (long)number;
                }

                case JTokenType.String:
                    if (!long.TryParse(token.Value<string>(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var parsed))
                    {
                        error = $"'{name}' must be an integer; got '{token.Value<string>()}'.";
                        return null;
                    }

                    return parsed;

                default:
                    error = $"'{name}' must be an integer; got {token.Type}.";
                    return null;
            }
        }

        private static string FormatResult(IReadOnlyList<string> urls, string configurationLabel, long? seed)
        {
            var builder = new StringBuilder();
            builder.Append("Generated ").Append(urls.Count).Append(" image(s) (")
                .Append(configurationLabel).AppendLine(").");

            if (seed.HasValue)
                builder.Append("Seed: ").AppendLine(seed.Value.ToString(CultureInfo.InvariantCulture));

            builder.AppendLine(
                "The generated image(s) are attached to this tool result so you can inspect them.");
            builder.AppendLine(
                "Embed the following Markdown snippet(s) in your reply so the user can see the image(s):");

            for (var i = 0; i < urls.Count; i++)
            {
                builder.Append("![generated image ").Append(i + 1).Append("](").Append(urls[i])
                    .AppendLine(")");
                builder.Append("Image URL: ").AppendLine(urls[i]);
            }

            return builder.ToString().TrimEnd();
        }

        private static IComfyUiClient CreateComfyClient(ImageGenerationProfile profile)
        {
            var options = new ComfyUiClientOptions(
                profile.BaseUrl.Trim(),
                TimeSpan.FromSeconds(profile.TimeoutSeconds),
                TimeSpan.FromSeconds(profile.MaxWaitSeconds),
                TimeSpan.FromMilliseconds(profile.PollIntervalMilliseconds),
                profile.ApiKey);

            return new ComfyUiClient(ImageGenerationHttp.Client, options);
        }

        private static IA1111Client CreateA1111Client(ImageGenerationProfile profile)
        {
            var options = new A1111ClientOptions(
                profile.BaseUrl.Trim(),
                profile.ApiKey,
                TimeSpan.FromSeconds(profile.MaxWaitSeconds));

            return new A1111Client(ImageGenerationHttp.Client, options);
        }

        private sealed record StandardArguments(
            string Prompt,
            string NegativePrompt,
            string Model,
            int Width,
            int Height,
            int Steps,
            double CfgScale,
            string Sampler,
            string Scheduler,
            long Seed,
            int BatchSize);

        private sealed record GeneratedImage(string FileName, string ContentType, byte[] Content);
    }
}
