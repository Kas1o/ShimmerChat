using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.ImageGeneration;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class A1111ClientTests
{
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static A1111Client CreateClient(
        ComfyUiStubHttpHandler handler, string apiKey = "", int maxWaitSeconds = 5) =>
        new(new HttpClient(handler),
            new A1111ClientOptions(
                "http://a1111.test",
                apiKey,
                TimeSpan.FromSeconds(maxWaitSeconds)));

    private static A1111Txt2ImgRequest Request() => new(
        Prompt: "a cat",
        NegativePrompt: "blurry",
        Model: "sdxl.safetensors",
        Sampler: "Euler a",
        Width: 768,
        Height: 512,
        Steps: 20,
        CfgScale: 7,
        Seed: 42,
        BatchSize: 2);

    [Fact]
    public async Task GenerateAsync_PostsTxt2ImgPayloadAndDecodesImages()
    {
        JObject? payload = null;
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/sdapi/v1/txt2img")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            payload = JObject.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return ComfyUiStubHttpHandler.Json(
                $$"""{"images":["{{PngBase64}}"],"parameters":{},"info":"{}"}""");
        });

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Images.Should().ContainSingle();
        result.Images[0].ContentType.Should().Be("image/png");
        result.Images[0].FileName.Should().Be("a1111_0001.png");
        result.Images[0].Content.Length.Should().BeGreaterThan(8);

        payload.Should().NotBeNull();
        payload!["prompt"]!.Value<string>().Should().Be("a cat");
        payload["negative_prompt"]!.Value<string>().Should().Be("blurry");
        payload["sampler_name"]!.Value<string>().Should().Be("Euler a");
        payload["width"]!.Value<int>().Should().Be(768);
        payload["height"]!.Value<int>().Should().Be(512);
        payload["steps"]!.Value<int>().Should().Be(20);
        payload["cfg_scale"]!.Value<double>().Should().Be(7);
        payload["seed"]!.Value<long>().Should().Be(42);
        payload["batch_size"]!.Value<int>().Should().Be(2);
        payload["n_iter"]!.Value<int>().Should().Be(1);
        payload["do_not_save_samples"]!.Value<bool>().Should().BeTrue();
        payload["override_settings"]!["sd_model_checkpoint"]!.Value<string>()
            .Should().Be("sdxl.safetensors");
    }

    [Fact]
    public async Task GenerateAsync_AcceptsDataUriImages()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            $$"""{"images":["data:image/png;base64,{{PngBase64}}"]}"""));

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Images.Should().ContainSingle();
        result.Images[0].ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task GenerateAsync_OmitsOverrideSettingsWhenModelEmpty()
    {
        JObject? payload = null;
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            payload = JObject.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return ComfyUiStubHttpHandler.Json($$"""{"images":["{{PngBase64}}"]}""");
        });

        var request = Request() with { Model = "" };
        var result = await CreateClient(handler).GenerateAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        payload!.ContainsKey("override_settings").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_HttpError_FailsWithDetail()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            """{"detail":"model not found"}""", HttpStatusCode.BadRequest));

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("HTTP 400");
        result.Error.Should().Contain("model not found");
        result.Details.Should().Contain("model not found");
    }

    [Fact]
    public async Task GenerateAsync_NoImages_Fails()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            """{"images":[],"info":"{}"}"""));

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("returned no images");
    }

    [Fact]
    public async Task GenerateAsync_InvalidBase64_FailsWithIndex()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            """{"images":["not-base64!!"]}"""));

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("index 0");
        result.Error.Should().Contain("base64");
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedImageBytes_Fails()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            $$"""{"images":["{{Convert.ToBase64String(Encoding.UTF8.GetBytes("not an image"))}}"]}"""));

        var result = await CreateClient(handler).GenerateAsync(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported image");
    }

    [Fact]
    public async Task GenerateAsync_BasicCredential_IsSentAsBasicAuth()
    {
        string? authorization = null;
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return ComfyUiStubHttpHandler.Json($$"""{"images":["{{PngBase64}}"]}""");
        });

        await CreateClient(handler, apiKey: "user:password")
            .GenerateAsync(Request(), CancellationToken.None);

        authorization.Should().StartWith("Basic ");
    }

    [Fact]
    public async Task GenerateAsync_TokenCredential_IsSentAsBearer()
    {
        string? authorization = null;
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return ComfyUiStubHttpHandler.Json($$"""{"images":["{{PngBase64}}"]}""");
        });

        await CreateClient(handler, apiKey: "token-value")
            .GenerateAsync(Request(), CancellationToken.None);

        authorization.Should().Be("Bearer token-value");
    }

    [Fact]
    public async Task Probe_ReadsModelsAndSamplers()
    {
        var handler = new ComfyUiStubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/sdapi/v1/sd-models" => ComfyUiStubHttpHandler.Json(
                """[{"title":"sdxl [abc]","model_name":"sdxl"},{"title":"","model_name":"sd15"}]"""),
            "/sdapi/v1/samplers" => ComfyUiStubHttpHandler.Json(
                """[{"name":"Euler a"},{"name":"DPM++ 2M"}]"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var models = await A1111Probe.FetchCheckpointModelsAsync(
            "http://a1111.test", "", TimeSpan.FromSeconds(5), http: new HttpClient(handler));
        var samplers = await A1111Probe.FetchSamplersAsync(
            "http://a1111.test", "", TimeSpan.FromSeconds(5), http: new HttpClient(handler));

        models.Values.Should().Equal("sdxl", "sd15");
        samplers.Values.Should().Equal("Euler a", "DPM++ 2M");
    }

    [Fact]
    public async Task Probe_HttpError_ReturnsError()
    {
        var handler = new ComfyUiStubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("nope")
        });

        var (values, error) = await A1111Probe.FetchCheckpointModelsAsync(
            "http://a1111.test", "", TimeSpan.FromSeconds(5), http: new HttpClient(handler));

        values.Should().BeNull();
        error.Should().Contain("HTTP 401");
    }
}
