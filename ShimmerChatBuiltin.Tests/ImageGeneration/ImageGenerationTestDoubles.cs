using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.Util;
using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

/// <summary>返回 Key 本身的本地化桩，便于断言错误信息里出现的 Key。</summary>
internal sealed class ImageGenerationStubLocService : ILocService
{
    public string CurrentCulture => "en-US";
    public IReadOnlyList<string> SupportedCultures => ["en-US"];

    public string this[string key] => key;

    public string Format(string key, params object[] args) =>
        $"{key}[{string.Join(",", args.Select(a => a?.ToString() ?? "null"))}]";

    public string Format(string key, params (string name, object value)[] args) => key;

    public void SetCulture(string culture) { }
}

/// <summary>记录工作流并返回预置结果的 ComfyUI 客户端桩。</summary>
internal sealed class FakeComfyUiClient : IComfyUiClient
{
    public JObject? LastWorkflow { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    public ComfyUiGenerationResult Result { get; set; } = ComfyUiGenerationResult.Ok(
    [
        new ComfyUiImage
        {
            FileName = "ComfyUI_0001.png",
            ContentType = "image/png",
            Content = [0x89, 0x50, 0x4E, 0x47]
        }
    ]);

    public Func<JObject, CancellationToken, Task<ComfyUiGenerationResult>>? Handler { get; set; }

    public Task<ComfyUiGenerationResult> GenerateAsync(JObject workflow, CancellationToken ct)
    {
        LastWorkflow = workflow;
        LastCancellationToken = ct;
        return Handler != null ? Handler(workflow, ct) : Task.FromResult(Result);
    }
}

/// <summary>记录 txt2img 请求并返回预置结果的 A1111 客户端桩。</summary>
internal sealed class FakeA1111Client : IA1111Client
{
    public A1111Txt2ImgRequest? LastRequest { get; private set; }

    public A1111GenerationResult Result { get; set; } = A1111GenerationResult.Ok(
    [
        new A1111Image
        {
            FileName = "a1111_0001.png",
            ContentType = "image/png",
            Content = [0x89, 0x50, 0x4E, 0x47]
        }
    ]);

    public Func<A1111Txt2ImgRequest, CancellationToken, Task<A1111GenerationResult>>? Handler { get; set; }

    public Task<A1111GenerationResult> GenerateAsync(A1111Txt2ImgRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Handler != null ? Handler(request, ct) : Task.FromResult(Result);
    }
}

/// <summary>内存版图像附件服务：记录落盘内容，并返回稳定的 /userimages/ 链接。</summary>
internal sealed class FakeImageAttachmentService : IImageAttachmentService
{
    public List<(string ContentType, byte[] Content)> Saved { get; } = [];

    public bool ThrowOnSave { get; set; }

    public ImageStorageMode Mode => ImageStorageMode.UserUploadImage;

    public Task<MessageImage> SaveAsync(Stream content, string contentType) =>
        throw new NotSupportedException("Tests should call SaveGeneratedAsync.");

    public async Task<MessageImage> SaveGeneratedAsync(
        Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave)
            throw new IOException("Simulated disk failure.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Saved.Add((contentType, buffer.ToArray()));

        return MessageImage.FromFile($"{Saved.Count:D4}.png", contentType);
    }

    public Task<ChatImage> ToChatImageAsync(MessageImage image, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public string? GetDisplayUrl(MessageImage image) =>
        image.FileName == null ? null : $"/userimages/{image.FileName}";

    public void DeleteStoredFile(MessageImage image) { }
}

/// <summary>按请求路径返回预置响应的 HTTP 处理器，不发起真实网络请求。</summary>
internal sealed class ComfyUiStubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public ComfyUiStubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<string> RequestPaths { get; } = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri?.PathAndQuery ?? "");
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}

internal static class ImageGenerationTestData
{
    public const string CustomWorkflowId = "portrait";

    public const string A1111ProfileId = "a1111";

    public const string CustomWorkflowTemplate = """
        {
          "3": {
            "class_type": "KSampler",
            "inputs": {
              "seed": {{seed}},
              "steps": {{steps}},
              "text": "{{prompt}}",
              "style": "{{style}}"
            }
          },
          "4": {
            "class_type": "CheckpointLoaderSimple",
            "inputs": { "ckpt_name": "{{model}}" }
          }
        }
        """;

    public static ImageGenerationConfig CreateConfig(Action<ImageGenerationConfig>? configure = null)
    {
        var config = ImageGenerationConfig.CreateDefault();
        config.Profiles[0].DefaultWorkflow.Model = "sd_xl_base_1.0.safetensors";
        config.Profiles[0].DefaultWorkflow.NegativePrompt = "blurry";
        configure?.Invoke(config);
        return config;
    }

    public static string ConfigJson(Action<ImageGenerationConfig>? configure = null) =>
        JsonConvert.SerializeObject(CreateConfig(configure));

    /// <summary>旧版（Version 0）配置 JSON，用于验证迁移。</summary>
    public static string LegacyConfigJson(
        bool includeDefault = true,
        IReadOnlyList<CustomImageWorkflow>? customWorkflows = null) =>
        JsonConvert.SerializeObject(new
        {
            ComfyUiBaseUrl = "http://legacy.test:8188",
            TimeoutSeconds = 15,
            MaxWaitSeconds = 120,
            PollIntervalMilliseconds = 500,
            DefaultWorkflow = includeDefault
                ? new DefaultImageWorkflowSettings
                {
                    Model = "legacy.safetensors",
                    Width = 640,
                    Height = 768,
                    Steps = 18,
                    CfgScale = 6.5,
                    Sampler = "dpmpp_2m",
                    Scheduler = "karras",
                    NegativePrompt = "legacy negative",
                    BatchSize = 2
                }
                : null,
            CustomWorkflows = customWorkflows
        });

    public static ImageGenerationProfile DefaultProfile(this ImageGenerationConfig config) =>
        config.Profiles.Single(p => p.Id == ImageGenerationConfigManager.DefaultProfileId);

    public static CustomImageWorkflow CreateCustomWorkflow(bool enabled = true) => new()
    {
        Id = CustomWorkflowId,
        Name = "Portrait",
        Description = "Portrait workflow",
        Enabled = enabled,
        WorkflowJson = CustomWorkflowTemplate,
        Parameters =
        [
            new ImageWorkflowParameter
            {
                Name = "prompt",
                Type = ImageWorkflowParameterType.String,
                Description = "Positive prompt",
                Required = true
            },
            new ImageWorkflowParameter
            {
                Name = "model",
                Type = ImageWorkflowParameterType.String,
                Description = "Checkpoint",
                Required = true
            },
            new ImageWorkflowParameter
            {
                Name = "seed",
                Type = ImageWorkflowParameterType.Integer,
                Description = "Seed",
                Required = true,
                Min = 0
            },
            new ImageWorkflowParameter
            {
                Name = "steps",
                Type = ImageWorkflowParameterType.Integer,
                Description = "Steps",
                Required = false,
                DefaultValue = "24",
                Min = 1,
                Max = 150
            },
            new ImageWorkflowParameter
            {
                Name = "style",
                Type = ImageWorkflowParameterType.Enum,
                Description = "Style",
                Required = false,
                DefaultValue = "anime",
                EnumValues = ["anime", "photo"]
            }
        ]
    };

    public static ImageGenerationProfile CreateCustomProfile(bool enabled = true) => new()
    {
        Id = CustomWorkflowId,
        Name = "Portrait",
        Description = "Portrait workflow",
        Enabled = enabled,
        Provider = ImageApiProvider.ComfyUi,
        BaseUrl = "http://127.0.0.1:8188",
        WorkflowMode = ImageWorkflowMode.Custom,
        DefaultWorkflow = new DefaultImageWorkflowSettings
        {
            Model = "sd_xl_base_1.0.safetensors",
            Width = 512,
            Height = 512,
            Steps = 20,
            CfgScale = 7,
            Sampler = "euler",
            Scheduler = "normal",
            NegativePrompt = "blurry",
            BatchSize = 1
        },
        CustomWorkflowJson = CustomWorkflowTemplate,
        CustomWorkflowParameters = CreateCustomWorkflow().Parameters
    };

    public static ImageGenerationProfile CreateA1111Profile(bool enabled = true) => new()
    {
        Id = A1111ProfileId,
        Name = "A1111 shared",
        Description = "Shared A1111 endpoint",
        Enabled = enabled,
        Provider = ImageApiProvider.Automatic1111,
        BaseUrl = "http://127.0.0.1:7860",
        TimeoutSeconds = 20,
        MaxWaitSeconds = 180,
        PollIntervalMilliseconds = 1000,
        WorkflowMode = ImageWorkflowMode.Default,
        DefaultWorkflow = new DefaultImageWorkflowSettings
        {
            Model = "",
            Width = 512,
            Height = 512,
            Steps = 28,
            CfgScale = 8,
            Sampler = "Euler a",
            Scheduler = "",
            NegativePrompt = "low quality",
            BatchSize = 3
        }
    };

    public static ToolInvocationContext Arguments(string json) => new()
    {
        ToolCallId = "call_1",
        ToolName = "generate_image",
        ArgumentsJson = json,
        RoundIndex = 0
    };
}
