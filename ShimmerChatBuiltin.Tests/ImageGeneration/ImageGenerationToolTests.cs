using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatBuiltin.Tests.SubAgent;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class ImageGenerationToolTests
{
    private static (ImageGenerationToolV2 Tool, FakeComfyUiClient Client,
        FakeImageAttachmentService Attachments, FakeKVDataService Kv)
        CreateDefaultTool(Action<ImageGenerationConfig>? configure = null, bool throwOnSave = false)
    {
        var kv = new FakeKVDataService();
        kv.Write("ImageGeneration", "Config", ImageGenerationTestData.ConfigJson(configure));

        var client = new FakeComfyUiClient();
        var attachments = new FakeImageAttachmentService { ThrowOnSave = throwOnSave };
        var tool = new ImageGenerationToolV2(kv, attachments, customWorkflowId: null, _ => client);

        return (tool, client, attachments, kv);
    }

    private static (ImageGenerationToolV2 Tool, FakeComfyUiClient Client,
        FakeImageAttachmentService Attachments, FakeKVDataService Kv)
        CreateCustomTool(Action<ImageGenerationConfig>? configure = null)
    {
        var kv = new FakeKVDataService();
        kv.Write("ImageGeneration", "Config", ImageGenerationTestData.ConfigJson(c =>
        {
            c.CustomWorkflows = [ImageGenerationTestData.CreateCustomWorkflow()];
            configure?.Invoke(c);
        }));

        var client = new FakeComfyUiClient();
        var attachments = new FakeImageAttachmentService();
        var tool = new ImageGenerationToolV2(
            kv, attachments, ImageGenerationTestData.CustomWorkflowId, _ => client);

        return (tool, client, attachments, kv);
    }

    [Fact]
    public async Task DefaultWorkflow_UsesArgumentsAndReturnsMarkdownLink()
    {
        var (tool, client, attachments, _) = CreateDefaultTool();

        var result = await tool.ExecuteAsync(ImageGenerationTestData.Arguments("""
            {
              "prompt": "a cat",
              "width": 768,
              "height": 512,
              "steps": 12,
              "cfg": 5.5,
              "sampler": "dpmpp_2m",
              "scheduler": "karras",
              "seed": 123,
              "batch_size": 2
            }
            """), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("Generated 1 image(s)");
        result.Content.Should().Contain("![generated image 1](/userimages/0001.png)");
        result.Content.Should().Contain("Image URL: /userimages/0001.png");
        result.Content.Should().Contain("Seed: 123");
        result.Content.Should().Contain("attached to this tool result");

        // 链接用于回复展示，Images 用于让模型在下一轮真正看到图像
        result.Images.Should().ContainSingle();
        result.Images![0].FileName.Should().Be("0001.png");
        result.Images[0].MimeType.Should().Be("image/png");

        attachments.Saved.Should().ContainSingle();
        attachments.Saved[0].ContentType.Should().Be("image/png");
        attachments.Saved[0].Content.Should().Equal(0x89, 0x50, 0x4E, 0x47);

        var workflow = client.LastWorkflow!;
        workflow["4"]!["inputs"]!["ckpt_name"]!.Value<string>()
            .Should().Be("sd_xl_base_1.0.safetensors");
        workflow["5"]!["inputs"]!["width"]!.Value<int>().Should().Be(768);
        workflow["5"]!["inputs"]!["height"]!.Value<int>().Should().Be(512);
        workflow["5"]!["inputs"]!["batch_size"]!.Value<int>().Should().Be(2);
        workflow["3"]!["inputs"]!["steps"]!.Value<int>().Should().Be(12);
        workflow["3"]!["inputs"]!["cfg"]!.Value<double>().Should().Be(5.5);
        workflow["3"]!["inputs"]!["sampler_name"]!.Value<string>().Should().Be("dpmpp_2m");
        workflow["3"]!["inputs"]!["scheduler"]!.Value<string>().Should().Be("karras");
        workflow["3"]!["inputs"]!["seed"]!.Value<long>().Should().Be(123);
        workflow["6"]!["inputs"]!["text"]!.Value<string>().Should().Be("a cat");
        workflow["7"]!["inputs"]!["text"]!.Value<string>().Should().Be("blurry");
    }

    [Fact]
    public async Task DefaultWorkflow_MissingPrompt_FailsWithStructuredError()
    {
        var (tool, client, _, _) = CreateDefaultTool();

        var result = await tool.ExecuteAsync(ImageGenerationTestData.Arguments("{}"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("'prompt' is required");
        client.LastWorkflow.Should().BeNull();
    }

    [Fact]
    public async Task DefaultWorkflow_WidthNotMultipleOfEight_Fails()
    {
        var (tool, _, _, _) = CreateDefaultTool();

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments("""{"prompt":"a cat","width":500}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("multiple of 8");
    }

    [Fact]
    public async Task DefaultWorkflow_UnknownArgument_Fails()
    {
        var (tool, _, _, _) = CreateDefaultTool();

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments("""{"prompt":"a cat","surprise":1}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("Unknown parameter(s): surprise");
    }

    [Fact]
    public async Task InvalidConfig_FailsWithConfigError()
    {
        var kv = new FakeKVDataService();
        kv.Write("ImageGeneration", "Config", "{ broken");
        var tool = new ImageGenerationToolV2(
            kv, new FakeImageAttachmentService(), customWorkflowId: null, _ => new FakeComfyUiClient());

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments("""{"prompt":"a cat"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
        result.Details.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task MissingModel_FailsAndTellsUserToConfigureIt()
    {
        var (tool, _, _, _) = CreateDefaultTool(c => c.DefaultWorkflow.Model = "");

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments("""{"prompt":"a cat"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("No checkpoint model is configured");
    }

    [Fact]
    public async Task SaveFailure_FailsWithOutputWriteError()
    {
        var (tool, _, _, _) = CreateDefaultTool(throwOnSave: true);

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments("""{"prompt":"a cat"}"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.OutputWriteFailed);
        result.Details.Should().Contain("Simulated disk failure");
    }

    [Fact]
    public void CustomWorkflow_DefinitionExposesDeclaredParameters()
    {
        var (tool, _, _, _) = CreateCustomTool();

        var definition = tool.GetDefinition();

        definition.name.Should().Be("generate_image_portrait");
        definition.description.Should().Contain("Portrait");
        definition.description.Should().Contain("embed");

        var parameters = definition.parameters!;
        parameters.Select(p => p.parameter.name)
            .Should().Equal("prompt", "model", "seed", "steps", "style");
        parameters.Single(p => p.parameter.name == "prompt").required.Should().BeTrue();
        parameters.Single(p => p.parameter.name == "steps").required.Should().BeFalse();
        parameters.Single(p => p.parameter.name == "style").parameter.@enum
            .Should().Equal("anime", "photo");
    }

    [Fact]
    public async Task CustomWorkflow_RendersTemplateWithDefaults()
    {
        var (tool, client, attachments, _) = CreateCustomTool();

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments(
                """{"prompt":"a portrait","model":"sdxl.safetensors","seed":42}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("Portrait");
        attachments.Saved.Should().ContainSingle();
        result.Images.Should().ContainSingle();
        result.Images![0].FileName.Should().Be("0001.png");

        var workflow = client.LastWorkflow!;
        workflow["3"]!["inputs"]!["seed"]!.Value<long>().Should().Be(42);
        workflow["3"]!["inputs"]!["steps"]!.Value<long>().Should().Be(24);
        workflow["3"]!["inputs"]!["text"]!.Value<string>().Should().Be("a portrait");
        workflow["3"]!["inputs"]!["style"]!.Value<string>().Should().Be("anime");
        workflow["4"]!["inputs"]!["ckpt_name"]!.Value<string>().Should().Be("sdxl.safetensors");
    }

    [Fact]
    public async Task CustomWorkflow_UnknownArgument_Fails()
    {
        var (tool, _, _, _) = CreateCustomTool();

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments(
                """{"prompt":"a portrait","model":"sdxl","seed":42,"unexpected":true}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("Unknown parameter(s): unexpected");
    }

    [Fact]
    public async Task CustomWorkflow_Disabled_Fails()
    {
        var (tool, _, _, _) = CreateCustomTool(c =>
            c.CustomWorkflows[0].Enabled = false);

        var result = await tool.ExecuteAsync(
            ImageGenerationTestData.Arguments(
                """{"prompt":"a portrait","model":"sdxl","seed":42}"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
        result.ErrorMessage.Should().Contain("disabled");
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        var (tool, client, _, _) = CreateDefaultTool();
        client.Handler = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(client.Result);
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tool.ExecuteAsync(
                ImageGenerationTestData.Arguments("""{"prompt":"a cat"}"""), cts.Token));
    }
}
