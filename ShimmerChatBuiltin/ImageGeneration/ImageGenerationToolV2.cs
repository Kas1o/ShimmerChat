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
    /// 文生图工具。同一个实现服务两种工作流：
    /// <list type="bullet">
    /// <item>默认工作流（<c>_customWorkflowId == null</c>）：模型、宽高、采样参数可由工具参数覆盖；</item>
    /// <item>自定义工作流：工具形参完全来自工作流配置中的参数声明，工作流 JSON 由模板引擎替换。</item>
    /// </list>
    /// 生成结果经 <see cref="IImageAttachmentService.SaveGeneratedAsync"/> 落到 UserUploadImage，
    /// 返回内容里带上 <c>/userimages/...</c> 的 Markdown 链接，供模型直接嵌入回复。
    /// </summary>
    public sealed class ImageGenerationToolV2 : IAutoCreateToolV2, IToolV3
    {
        public const string DefaultToolName = "generate_image";

        public static string NameKey => "tool.generate_image";
        public static string DescriptionKey => "tool.generate_image.desc";
        public static string[] CategoryKeys => ["category.image_generation", "category.content"];

        public static IAutoCreateToolV2 Create(PersistentEnv env) =>
            new ImageGenerationToolV2(env.KVData, env.ImageAttachments, customWorkflowId: null);

        private static readonly HashSet<string> DefaultArgumentNames = new(StringComparer.Ordinal)
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

        private readonly IImageAttachmentService? _imageAttachments;
        private readonly string? _customWorkflowId;
        private readonly Func<ImageGenerationConfig, IComfyUiClient> _clientFactory;
        private readonly ImageGenerationConfig? _config;
        private readonly string? _configError;

        public ImageGenerationToolV2(
            IKVDataService kvData,
            IImageAttachmentService? imageAttachments,
            string? customWorkflowId)
            : this(kvData, imageAttachments, customWorkflowId, clientFactory: null)
        {
        }

        internal ImageGenerationToolV2(
            IKVDataService kvData,
            IImageAttachmentService? imageAttachments,
            string? customWorkflowId,
            Func<ImageGenerationConfig, IComfyUiClient>? clientFactory)
        {
            _imageAttachments = imageAttachments;
            _customWorkflowId = customWorkflowId;
            _clientFactory = clientFactory ?? CreateClient;

            (_config, _configError) = ImageGenerationConfigManager.Load(kvData);
        }

        /// <summary>自定义工作流的工具名：<c>generate_image_{id}</c>。Id 已由配置校验限定字符集。</summary>
        public static string GetCustomToolName(string workflowId) =>
            $"{DefaultToolName}_{workflowId}";

        public Tool GetDefinition()
        {
            if (_customWorkflowId != null)
            {
                var workflow = _config?.FindCustomWorkflow(_customWorkflowId);
                if (workflow == null)
                {
                    return new Tool
                    {
                        name = GetCustomToolName(_customWorkflowId),
                        description =
                            $"Custom image workflow '{_customWorkflowId}' is not configured. " +
                            "Ask the user to check the Image Generation settings before using this tool.",
                        parameters = []
                    };
                }

                return new Tool
                {
                    name = GetCustomToolName(workflow.Id),
                    description = BuildCustomDescription(workflow),
                    parameters = BuildCustomParameters(workflow.Parameters)
                };
            }

            var defaults = _config?.DefaultWorkflow ?? new DefaultImageWorkflowSettings();
            return new Tool
            {
                name = DefaultToolName,
                description = BuildDefaultDescription(defaults),
                parameters = BuildDefaultParameters(defaults)
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

            if (_imageAttachments == null)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ConfigInvalid,
                    "Image storage is unavailable in the current session, so generated images cannot be saved.");
            }

            var arguments = context.TryGetArgumentsObject();
            if (arguments == null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "Arguments must be a JSON object.");

            JObject workflow;
            string workflowLabel;
            long? seed = null;

            if (_customWorkflowId == null)
            {
                var (request, error) = ResolveDefaultArguments(arguments, _config);
                if (request == null)
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, error!);

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

                workflowLabel = "default workflow";
                seed = request.Seed;
            }
            else
            {
                var customWorkflow = _config.FindCustomWorkflow(_customWorkflowId);
                if (customWorkflow == null)
                {
                    return ToolResult.Fail(
                        ToolErrorCodes.ConfigInvalid,
                        $"Custom image workflow '{_customWorkflowId}' is not configured.");
                }

                if (!customWorkflow.Enabled)
                {
                    return ToolResult.Fail(
                        ToolErrorCodes.ConfigInvalid,
                        $"Custom image workflow '{customWorkflow.Name}' is disabled in the Image Generation settings.");
                }

                var (values, bindError) = WorkflowTemplateEngine.Bind(customWorkflow.Parameters, arguments);
                if (values == null)
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, bindError!);

                var (rendered, renderError) =
                    WorkflowTemplateEngine.Render(customWorkflow.WorkflowJson, values);
                if (rendered == null)
                {
                    return ToolResult.Fail(
                        ToolErrorCodes.ConfigInvalid,
                        $"Custom image workflow '{customWorkflow.Name}' could not be rendered.",
                        renderError);
                }

                workflow = rendered;
                workflowLabel = customWorkflow.Name;
            }

            var client = _clientFactory(_config);
            var generation = await client.GenerateAsync(workflow, ct);

            if (!generation.Success)
            {
                return ToolResult.Fail(
                    ToolErrorCodes.ServiceError,
                    $"ComfyUI image generation failed: {generation.Error}",
                    generation.Details);
            }

            var links = new List<string>(generation.Images.Count);
            var savedImages = new List<MessageImage>(generation.Images.Count);

            foreach (var image in generation.Images)
            {
                MessageImage saved;
                try
                {
                    using var stream = new MemoryStream(image.Content, writable: false);
                    saved = await _imageAttachments.SaveGeneratedAsync(stream, image.ContentType, ct);
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
            return ToolResult.Ok(FormatResult(links, workflowLabel, seed), savedImages);
        }

        private static string BuildDefaultDescription(DefaultImageWorkflowSettings settings)
        {
            var model = string.IsNullOrWhiteSpace(settings.Model)
                ? "not configured"
                : settings.Model;

            return "Generate an image from a text prompt with ComfyUI using the default workflow. " +
                   $"Configured checkpoint: {model}. Width, height, steps, CFG, sampler, scheduler, seed and batch size " +
                   "can be overridden per call. On success the tool returns /userimages/... links; " +
                   "embed the returned Markdown image snippet in your reply so the user can see the image.";
        }

        private static string BuildCustomDescription(CustomImageWorkflow workflow)
        {
            var description =
                $"Generate an image with the ComfyUI custom workflow '{workflow.Name}'.";

            if (!string.IsNullOrWhiteSpace(workflow.Description))
                description += " " + workflow.Description.Trim();

            return description +
                   " On success the tool returns /userimages/... links; embed the returned Markdown image " +
                   "snippet in your reply so the user can see the image.";
        }

        private static List<(ToolParameter Parameter, bool Required)> BuildDefaultParameters(
            DefaultImageWorkflowSettings settings)
        {
            return
            [
                (Param("prompt", ParameterType.String,
                    "Text prompt describing the image to generate."), true),

                (Param("negative_prompt", ParameterType.String,
                    "Things to avoid in the image. Defaults to the configured negative prompt" +
                    (string.IsNullOrWhiteSpace(settings.NegativePrompt)
                        ? " (currently empty)."
                        : $": {settings.NegativePrompt}")), false),

                (Param("model", ParameterType.String,
                    string.IsNullOrWhiteSpace(settings.Model)
                        ? "Checkpoint name (ckpt_name) to load. Required: no default model is configured."
                        : $"Checkpoint name (ckpt_name) to load. Defaults to '{settings.Model}'."), false),

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

                (Param("scheduler", ParameterType.String,
                    $"Scheduler name, e.g. normal / karras. Defaults to '{settings.Scheduler}'."), false),

                (Param("seed", ParameterType.Number,
                    "Random seed. Omit it to pick a random seed for each call."), false),

                (Param("batch_size", ParameterType.Number,
                    $"Number of images to generate in one batch ({ImageGenerationConfigManager.MinBatchSize}-" +
                    $"{ImageGenerationConfigManager.MaxBatchSize}). Defaults to {settings.BatchSize}."), false)
            ];
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

        private static (DefaultArguments? Request, string? Error) ResolveDefaultArguments(
            JObject arguments, ImageGenerationConfig config)
        {
            var unknown = arguments.Properties()
                .Where(p => !DefaultArgumentNames.Contains(p.Name))
                .Select(p => p.Name)
                .ToList();

            if (unknown.Count > 0)
            {
                return (null,
                    $"Unknown parameter(s): {string.Join(", ", unknown)}. " +
                    $"Supported parameters: {string.Join(", ", DefaultArgumentNames)}.");
            }

            var prompt = ReadString(arguments, "prompt", out var error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(prompt))
                return (null, "'prompt' is required and must be a non-empty string.");

            var negativePrompt = ReadString(arguments, "negative_prompt", out error);
            if (error != null) return (null, error);
            negativePrompt ??= config.DefaultWorkflow.NegativePrompt;

            var model = ReadString(arguments, "model", out error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(model))
                model = config.DefaultWorkflow.Model;

            if (string.IsNullOrWhiteSpace(model))
            {
                return (null,
                    "No checkpoint model is configured for the default image workflow. " +
                    "Ask the user to select one in Settings → Image Generation.");
            }

            var width = ReadInt(arguments, "width", out error);
            if (error != null) return (null, error);
            width ??= config.DefaultWorkflow.Width;

            var height = ReadInt(arguments, "height", out error);
            if (error != null) return (null, error);
            height ??= config.DefaultWorkflow.Height;

            var steps = ReadInt(arguments, "steps", out error);
            if (error != null) return (null, error);
            steps ??= config.DefaultWorkflow.Steps;

            var cfg = ReadDouble(arguments, "cfg", out error);
            if (error != null) return (null, error);
            cfg ??= config.DefaultWorkflow.CfgScale;

            var sampler = ReadString(arguments, "sampler", out error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(sampler))
                sampler = config.DefaultWorkflow.Sampler;

            var scheduler = ReadString(arguments, "scheduler", out error);
            if (error != null) return (null, error);
            if (string.IsNullOrWhiteSpace(scheduler))
                scheduler = config.DefaultWorkflow.Scheduler;

            var seed = ReadLong(arguments, "seed", out error);
            if (error != null) return (null, error);
            seed ??= Random.Shared.NextInt64(0, long.MaxValue);

            var batchSize = ReadInt(arguments, "batch_size", out error);
            if (error != null) return (null, error);
            batchSize ??= config.DefaultWorkflow.BatchSize;

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

            return (new DefaultArguments(
                prompt!,
                negativePrompt!,
                model!,
                width.Value,
                height.Value,
                steps.Value,
                cfg.Value,
                sampler!,
                scheduler!,
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

        private static string FormatResult(IReadOnlyList<string> urls, string workflowLabel, long? seed)
        {
            var builder = new StringBuilder();
            builder.Append("Generated ").Append(urls.Count).Append(" image(s) with ComfyUI (")
                .Append(workflowLabel).AppendLine(").");

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

        private static IComfyUiClient CreateClient(ImageGenerationConfig config)
        {
            var options = new ComfyUiClientOptions(
                config.ComfyUiBaseUrl.Trim(),
                TimeSpan.FromSeconds(config.TimeoutSeconds),
                TimeSpan.FromSeconds(config.MaxWaitSeconds),
                TimeSpan.FromMilliseconds(config.PollIntervalMilliseconds));

            return new ComfyUiClient(ImageGenerationHttp.Client, options);
        }

        private sealed record DefaultArguments(
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
    }
}
