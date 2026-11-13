using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin.CodeAct;
using ShimmerChatBuiltin.Tests.ImageGeneration;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.CodeAct;

/// <summary>
/// CodeAct 工具的核心契约：脚本执行、工具桥接、图像输出开关、取消与结构化失败。
/// </summary>
public class CodeActToolTests
{
    private const string OnePixelPngBase64Header =
        "iVBORw0KGgo="; // 只需 PNG 文件头；工具按文件头识别格式，不解析像素

    private static ToolInvocationContext Args(string code, string? imports = null)
    {
        var json = imports == null
            ? $$"""{"code":{{System.Text.Json.JsonSerializer.Serialize(code)}}}"""
            : $$"""{"code":{{System.Text.Json.JsonSerializer.Serialize(code)}},"using":{{System.Text.Json.JsonSerializer.Serialize(imports)}}}""";

        return new ToolInvocationContext
        {
            ToolCallId = "call_codeact",
            ToolName = CodeActToolV2.ToolName,
            ArgumentsJson = json,
            RoundIndex = 0
        };
    }

    private static PersistentEnv CreateEnv(IImageAttachmentService? images = null)
    {
        return new PersistentEnv
        {
            KVData = new Mock<IKVDataService>().Object,
            ToolRegistry = new Mock<IToolRegistry>().Object,
            Serializer = new Mock<IPreGenerationNodeSerializer>().Object,
            LocService = new Mock<ILocService>().Object,
            DebugOutput = new Mock<IDebugOutputService>().Object,
            ImageAttachments = images,
            Chat = new Chat { Name = "CodeActTest" },
            Agent = Agent.Create("CodeActTest", "")
        };
    }

    private static StubToolV3 Stub(string name,
        Func<ToolInvocationContext, CancellationToken, Task<ToolResult>> handler)
        => new(name, handler);

    [Fact]
    public async Task Execute_RunsScript_AndReturnsLastExpression()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(Args("var x = 40; x + 2"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("42");
        result.Images.Should().BeNull();
    }

    [Fact]
    public async Task Execute_PrintOutput_IsAppendedAfterReturnValue()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(Args("""Print("hello"); 7"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("7");
        result.Content.Should().Contain("[stdout]");
        result.Content.Should().Contain("hello");
    }

    [Fact]
    public async Task Execute_CanCallAnotherTool_WithObjectArguments()
    {
        ToolInvocationContext? seen = null;
        var echo = Stub("echo", (context, _) =>
        {
            seen = context;
            return Task.FromResult(ToolResult.Ok("echo:ok"));
        });

        var tool = new CodeActToolV2([echo], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""await CallAsync("echo", new { message = "hi" })"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("echo:ok");

        seen.Should().NotBeNull();
        seen!.ToolName.Should().Be("echo");
        seen.ArgumentsJson.Should().Contain("\"message\"");
        seen.ArgumentsJson.Should().Contain("\"hi\"");
        seen.RoundIndex.Should().Be(0);
    }

    [Fact]
    public async Task Execute_CallAsync_TurnsToolFailureIntoStructuredCodeActFailure()
    {
        var broken = Stub("broken", (_, _) =>
            Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments, "missing 'query'")));

        var tool = new CodeActToolV2([broken], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""await CallAsync("broken")"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("missing 'query'");
    }

    [Fact]
    public async Task Execute_CallToolAsync_ExposesFailureWithoutThrowing()
    {
        var broken = Stub("broken", (_, _) =>
            Task.FromResult(ToolResult.Fail(ToolErrorCodes.ServiceError, "provider down")));

        var tool = new CodeActToolV2([broken], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""
                var r = await CallToolAsync("broken");
                r.Status + "|" + r.ErrorCode
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("Failed|TOOL_SERVICE_ERROR");
    }

    [Fact]
    public async Task Execute_DoesNotForwardToolImagesAutomatically()
    {
        var imageTool = Stub("read_image", (_, _) => Task.FromResult(
            ToolResult.Ok("image attached", [MessageImage.FromFile("a.png", "image/png")])));

        var tool = new CodeActToolV2([imageTool], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""await CallAsync("read_image")"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Images.Should().BeNull();
    }

    [Fact]
    public async Task Execute_ImagesAddResult_PutsToolImagesIntoTheContainer()
    {
        var imageTool = Stub("read_image", (_, _) => Task.FromResult(
            ToolResult.Ok("image attached", [MessageImage.FromFile("a.png", "image/png")])));

        var tool = new CodeActToolV2([imageTool], CreateEnv(new FakeImageAttachmentService()));

        var result = await tool.ExecuteAsync(
            Args("""
                var r = await CallToolAsync("read_image");
                Images.Add(r);
                r.Content
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("image attached");
        result.Images.Should().ContainSingle();
        result.Images![0].FileName.Should().Be("a.png");
    }

    [Fact]
    public async Task Execute_ImagesAddSingleImage_BuildsInlineDataUrl()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args($$"""
                Images.Add(MessageImage.FromBase64("{{OnePixelPngBase64Header}}", "image/png"));
                Images.Count + "|" + Images.Urls[0]
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("1|data:image/png;base64,");
        result.Images.Should().ContainSingle();
    }

    [Fact]
    public async Task Execute_ImagesContainer_CanFilterWhichToolImagesAreReturned()
    {
        var readTool = Stub("read_image", (_, _) => Task.FromResult(
            ToolResult.Ok("read", [MessageImage.FromFile("a.png", "image/png")])));
        var drawTool = Stub("generate_image", (_, _) => Task.FromResult(
            ToolResult.Ok("generated", [MessageImage.FromFile("b.png", "image/png")])));

        var tool = new CodeActToolV2([readTool, drawTool], CreateEnv(new FakeImageAttachmentService()));

        var result = await tool.ExecuteAsync(
            Args("""
                var a = await CallToolAsync("read_image");
                var b = await CallToolAsync("generate_image");
                Images.Add(b);
                a.Content + "|" + b.Content
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("read|generated");
        result.Images.Should().ContainSingle();
        result.Images![0].FileName.Should().Be("b.png");
    }

    [Fact]
    public async Task Execute_ImageOutputDisabled_BlocksAddingImages()
    {
        var imageTool = Stub("read_image", (_, _) => Task.FromResult(
            ToolResult.Ok("image attached", [MessageImage.FromFile("a.png", "image/png")])));

        var tool = new CodeActToolV2([imageTool], CreateEnv(), imageOutputEnabled: false);

        var toolImagesStillVisible = await tool.ExecuteAsync(
            Args("""
                var r = await CallToolAsync("read_image");
                r.Images.Count
                """),
            CancellationToken.None);

        // 工具结果本身仍可被脚本检查，只是不能放进返回容器。
        toolImagesStillVisible.Status.Should().Be(ToolResultStatus.Completed);
        toolImagesStillVisible.Content.Should().Be("1");
        toolImagesStillVisible.Images.Should().BeNull();

        var addResult = await tool.ExecuteAsync(
            Args("""
                var r = await CallToolAsync("read_image");
                Images.Add(r)
                """),
            CancellationToken.None);

        addResult.Status.Should().Be(ToolResultStatus.Failed);
        addResult.ErrorCode.Should().Be(ToolErrorCodes.ImageOutputDisabled);

        var addImage = await tool.ExecuteAsync(
            Args($$"""await AddImageAsync(Convert.FromBase64String("{{OnePixelPngBase64Header}}"), "image/png")"""),
            CancellationToken.None);

        addImage.Status.Should().Be(ToolResultStatus.Failed);
        addImage.ErrorCode.Should().Be(ToolErrorCodes.ImageOutputDisabled);
    }

    [Fact]
    public async Task Execute_ImagesContainer_RejectsMoreThanTheLimit()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args($$"""
                var image = MessageImage.FromBase64("{{OnePixelPngBase64Header}}", "image/png");
                for (var i = 0; i <= CodeActGlobals.MaxImages; i++)
                    Images.Add(image);
                0
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.OutputWriteFailed);
        result.ErrorMessage.Should().Contain("Image limit reached");
    }

    [Fact]
    public async Task Execute_ImagesAddStoredImageWithoutStorage_FailsExplicitly()
    {
        var tool = new CodeActToolV2([], CreateEnv(images: null));

        var result = await tool.ExecuteAsync(
            Args("""Images.Add(MessageImage.FromFile("missing.png", "image/png"))"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
        result.ErrorMessage.Should().Contain("image storage is unavailable");
    }

    [Fact]
    public async Task Execute_AddImage_SavesImageAndReturnsDisplayUrl()
    {
        var images = new FakeImageAttachmentService();
        var tool = new CodeActToolV2([], CreateEnv(images));

        var result = await tool.ExecuteAsync(
            Args($$"""
                var url = await AddImageAsync(Convert.FromBase64String("{{OnePixelPngBase64Header}}"), "image/png");
                url
                """),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("/userimages/");
        result.Images.Should().ContainSingle();
        images.Saved.Should().ContainSingle();
        images.Saved[0].ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Execute_AddImageWithoutStorage_FailsExplicitly()
    {
        var tool = new CodeActToolV2([], CreateEnv(images: null));

        var result = await tool.ExecuteAsync(
            Args($$"""await AddImageAsync(Convert.FromBase64String("{{OnePixelPngBase64Header}}"), "image/png")"""),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
    }

    [Fact]
    public async Task Execute_CompilationError_IsReportedWithDiagnostics()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(Args("var x = ;"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.CodeCompilationError);
        result.ErrorMessage.Should().Contain("failed to compile");
        result.Details.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Execute_RuntimeException_IsReportedAsCodeExecutionError()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""throw new InvalidOperationException("boom");"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.CodeExecutionError);
        result.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task Execute_UserCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var tool = new CodeActToolV2([], CreateEnv());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(Args("1 + 1"), cts.Token));
    }

    [Fact]
    public async Task Execute_UnawaitedTaskReturnValue_IsUnwrapped()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(Args("Task.FromResult(5)"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("5");
    }

    [Fact]
    public async Task Execute_UsingParameter_AddsExtraImports()
    {
        var tool = new CodeActToolV2([], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""Regex.IsMatch("abc", "^a")""", "System.Text.RegularExpressions"),
            CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("true");
    }

    [Fact]
    public async Task Execute_UnknownTool_ListsAvailableTools()
    {
        var echo = Stub("echo", (_, _) => Task.FromResult(ToolResult.Ok("ok")));
        var tool = new CodeActToolV2([echo], CreateEnv());

        var result = await tool.ExecuteAsync(
            Args("""await CallAsync("missing")"""), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("echo");
    }

    [Fact]
    public void GetDefinition_AdvertisesScriptApiAndAvailableTools()
    {
        var echo = Stub("echo", (_, _) => Task.FromResult(ToolResult.Ok("ok")));
        var tool = new CodeActToolV2([echo], CreateEnv());

        var definition = tool.GetDefinition();

        definition.name.Should().Be("codeact_csharp");
        definition.description.Should().Contain("CallAsync");
        definition.description.Should().Contain("AddImageAsync");
        definition.description.Should().Contain("echo");
        definition.parameters.Should().NotBeNull();
        definition.parameters!.Select(p => p.parameter.name)
            .Should().BeEquivalentTo(["code", "using"]);
    }

    [Fact]
    public async Task Execute_ToolsProperty_SeesToolsAddedAfterInstanceCreation()
    {
        var env = CreateEnv();
        var tools = new List<IToolV2>();
        var tool = new CodeActToolV2(tools, env);

        tools.Add(Stub("later_tool", (_, _) => Task.FromResult(ToolResult.Ok("late"))));

        var result = await tool.ExecuteAsync(Args("Tools"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain("later_tool");
    }

    private sealed class StubToolV3 : IToolV3
    {
        private readonly Func<ToolInvocationContext, CancellationToken, Task<ToolResult>> _handler;

        public StubToolV3(string name,
            Func<ToolInvocationContext, CancellationToken, Task<ToolResult>> handler)
        {
            Name = name;
            _handler = handler;
        }

        public string Name { get; }

        public Tool GetDefinition() => new()
        {
            name = Name,
            description = $"stub tool {Name}",
            parameters = []
        };

        public Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
            => _handler(context, ct);
    }
}
