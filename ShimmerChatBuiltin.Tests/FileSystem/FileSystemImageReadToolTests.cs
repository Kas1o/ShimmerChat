using Newtonsoft.Json;
using ShimmerChatBuiltin.Tests.SubAgent;

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

    private static ToolInvocationContext ContextFor(string path) => new()
    {
        ToolCallId = "call-1",
        ToolName = "read_image",
        ArgumentsJson = JsonConvert.SerializeObject(new { path }),
        RoundIndex = 0
    };

    [Fact]
    public async Task ExistingPng_ReturnsAttachedImageAndMetadata()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService());

        var result = await tool.ExecuteAsync(ContextFor(path), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Contain($"Image: {path}");
        result.Content.Should().Contain("Type: image/png");
        result.Content.Should().Contain("attached to this tool result");

        result.Images.Should().ContainSingle();
        result.Images![0].MimeType.Should().Be("image/png");
        result.Images[0].Base64.Should().Be(Convert.ToBase64String(PngBytes));
        result.Images[0].FileName.Should().BeNull();
    }

    [Fact]
    public async Task MissingPath_FailsWithInvalidArguments()
    {
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService());

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
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService());
        var path = Path.Combine(_directory, "nope.png");

        var result = await tool.ExecuteAsync(ContextFor(path), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.InvalidArguments);
        result.ErrorMessage.Should().Contain("File not found");
    }

    [Fact]
    public async Task NonImageContent_FailsInsteadOfGuessingFromExtension()
    {
        var path = WriteFile("fake.png", "this is plain text"u8.ToArray());
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService());

        var result = await tool.ExecuteAsync(ContextFor(path), CancellationToken.None);

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
        var tool = new FileSystemImageReadToolV2(kv);

        var result = await tool.ExecuteAsync(ContextFor(path), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Contain("Access denied");
    }

    [Fact]
    public async Task AlreadyCancelledToken_PropagatesCancellation()
    {
        var path = WriteFile("pixel.png", PngBytes);
        var tool = new FileSystemImageReadToolV2(new FakeKVDataService());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(ContextFor(path), cts.Token));
    }

    [Fact]
    public void DetectImageMimeType_RecognizesSupportedHeaders()
    {
        FileSystemImageReadToolV2.DetectImageMimeType([0xFF, 0xD8, 0xFF, 0xE0])
            .Should().Be("image/jpeg");
        FileSystemImageReadToolV2.DetectImageMimeType([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])
            .Should().Be("image/gif");
        FileSystemImageReadToolV2.DetectImageMimeType(
                [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50])
            .Should().Be("image/webp");
        FileSystemImageReadToolV2.DetectImageMimeType(
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
            .Should().Be("image/png");
    }

    [Fact]
    public void DetectImageMimeType_RejectsUnsupportedHeaders()
    {
        FileSystemImageReadToolV2.DetectImageMimeType([]).Should().BeNull();
        FileSystemImageReadToolV2.DetectImageMimeType([0x42, 0x4D, 0x00]).Should().BeNull();
        FileSystemImageReadToolV2.DetectImageMimeType([0x47, 0x49, 0x46, 0x38, 0x38, 0x61])
            .Should().BeNull();
    }
}
