using Newtonsoft.Json;
using ShimmerChatBuiltin.Tests.ImageGeneration;
using ShimmerChatBuiltin.Tests.SubAgent;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.Tests.FileSystem;

public class FileSystemImageReadToolTests : IDisposable
{
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D
    ];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "shimmer-image-read-" + Guid.NewGuid().ToString("N"));

    public FileSystemImageReadToolTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论；不吞掉断言失败。
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static ToolInvocationContext ContextFor(string path, string? processing, long? budget = null) => new()
    {
        ToolCallId = "call-1",
        ToolName = "read_image",
        ArgumentsJson = JsonConvert.SerializeObject(new
        {
            path,
            image_processing = processing,
            pixel_budget = budget
        }),
        RoundIndex = 0
    };

    [Fact]
    public async Task ExistingPng_SavesToFileAndReturnsMetadataWithLink()
    {
        // 新契约：不再内联 Base64（大图会撑爆消息存储），落盘后只带文件引用 + 稳定链接
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService();
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain($"Image: {path}");
        result.Content.Should().Contain("Type: image/png");
        result.Content.Should().Contain("Saved as: /userimages/");
        result.Content.Should().Contain("attached to this tool result");

        attachments.Saved.Should().ContainSingle()
            .Which.Content.Should().Equal(PngBytes);
        result.Images.Should().ContainSingle();
        result.Images![0].MimeType.Should().Be("image/png");
        result.Images[0].FileName.Should().NotBeNull("落盘附件只带文件引用");
        result.Images[0].Base64.Should().BeNull();
    }

    [Fact]
    public async Task AutoProcessing_FollowsTheGlobalUploadSetting()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService
        {
            UploadProcessing = new ImageProcessingOptions { PixelBudget = 1024L * 1024 }
        };
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(
            ContextFor(path, processing: "auto"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        attachments.SavedProcessings.Should().ContainSingle()
            .Which.PixelBudget.Should().Be(1024L * 1024);
    }

    [Fact]
    public async Task OriginalProcessing_SavesBytesAsIs()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService
        {
            UploadProcessing = new ImageProcessingOptions { PixelBudget = 1024L * 1024 }
        };
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(
            ContextFor(path, processing: "original"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        attachments.SavedProcessings.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task BudgetProcessing_UsesThePixelBudgetArgument()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService();
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(
            ContextFor(path, processing: "budget", budget: 2048L * 2048), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        attachments.SavedProcessings.Should().ContainSingle()
            .Which.PixelBudget.Should().Be(2048L * 2048);
    }

    [Fact]
    public async Task BudgetProcessing_ClampsOutOfRangeBudgetAndFallsBackToDefaults()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService();
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(
            ContextFor(path, processing: "budget", budget: -5), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        var saved = attachments.SavedProcessings.Should().ContainSingle().Subject;
        saved!.PixelBudget.Should().Be(ImageAttachmentRules.MinPixelBudget);
        saved.Algorithm.Should().Be(ImageCompressionAlgorithm.WebP);
        saved.Quality.Should().Be(ImageAttachmentRules.DefaultCompressionQuality);
    }

    [Fact]
    public async Task UnknownProcessingValue_FailsWithInvalidArguments()
    {
        // 工具参数非法必须显式失败，不猜测修正
        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), new FakeImageAttachmentService());

        var result = await tool.ExecuteAsync(
            ContextFor(path, processing: "maximize"), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("Unknown image_processing value 'maximize'");
    }

    [Fact]
    public async Task MissingImageService_FailsWithConfigInvalid()
    {
        // 与 CodeAct 一致：无图像存储的会话显式失败，不退回内联 Base64 掩盖问题
        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), imageAttachments: null);

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
        result.ErrorMessage.Should().Contain("Image storage is unavailable");
    }

    [Fact]
    public async Task SaveFailure_FailsWithOutputWriteFailed()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService { ThrowOnSave = true };
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.OutputWriteFailed);
    }

    [Fact]
    public async Task UnresolvableDisplayUrl_FailsInsteadOfReturningAnInvisibleImage()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var attachments = new FakeImageAttachmentService { ReturnNoDisplayUrl = true };
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), attachments);

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.OutputWriteFailed);
        result.ErrorMessage.Should().Contain("no display URL could be resolved");
    }

    [Fact]
    public async Task MissingPath_FailsWithInvalidArguments()
    {
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), new FakeImageAttachmentService());

        var result = await tool.ExecuteAsync(new ToolInvocationContext
        {
            ToolCallId = "call-1",
            ToolName = "read_image",
            ArgumentsJson = "{}",
            RoundIndex = 0
        }, CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("'path' is required");
    }

    [Fact]
    public async Task MissingFile_FailsWithInvalidArguments()
    {
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), new FakeImageAttachmentService());
        var path = Path.Combine(_directory, "nope.png");

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("File not found");
    }

    [Fact]
    public async Task NonImageContent_FailsInsteadOfGuessingFromExtension()
    {
        var path = WriteFile("fake.png", "this is plain text"u8.ToArray());
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), new FakeImageAttachmentService());

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("Unsupported or unrecognized image format");
        result.Images.Should().BeNull();
    }

    [Fact]
    public async Task BannedPath_FailsWithAccessDenied()
    {
        var kv = new FakeKVDataService();
        kv.Write("Filesystem", "Config",
            JsonConvert.SerializeObject(new FileSystemConfig { BanList = [".*"] }));

        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(kv, new FakeImageAttachmentService());

        var result = await tool.ExecuteAsync(ContextFor(path, processing: null), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Contain("Access denied");
    }

    [Fact]
    public async Task AlreadyCancelledToken_PropagatesCancellation()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService(), new FakeImageAttachmentService());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(ContextFor(path, processing: null), cts.Token));
    }

    [Fact]
    public void ImageMimeDetector_RecognizesSupportedHeaders()
    {
        ImageMimeDetector.Detect([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be("image/jpeg");
        ImageMimeDetector.Detect([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]).Should().Be("image/gif");
        ImageMimeDetector.Detect([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50])
            .Should().Be("image/webp");
        ImageMimeDetector.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
            .Should().Be("image/png");
    }

    [Fact]
    public void ImageMimeDetector_RejectsUnsupportedHeaders()
    {
        ImageMimeDetector.Detect([]).Should().BeNull();
        ImageMimeDetector.Detect([0x42, 0x4D, 0x00]).Should().BeNull();
        ImageMimeDetector.Detect([0x47, 0x49, 0x46, 0x38, 0x38, 0x61]).Should().BeNull();
    }
}
