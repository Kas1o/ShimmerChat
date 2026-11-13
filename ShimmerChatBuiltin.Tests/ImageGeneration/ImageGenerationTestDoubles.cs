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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri?.PathAndQuery ?? "");
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
        var config = new ImageGenerationConfig
        {
            ComfyUiBaseUrl = "http://127.0.0.1:8188",
            TimeoutSeconds = 30,
            MaxWaitSeconds = 300,
            PollIntervalMilliseconds = 1000,
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
            }
        };

        configure?.Invoke(config);
        return config;
    }

    public static string ConfigJson(Action<ImageGenerationConfig>? configure = null) =>
        JsonConvert.SerializeObject(CreateConfig(configure));

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

    public static ToolInvocationContext Arguments(string json) => new()
    {
        ToolCallId = "call_1",
        ToolName = "generate_image",
        ArgumentsJson = json,
        RoundIndex = 0
    };
}
