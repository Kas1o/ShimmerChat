namespace ShimmerChatLib.Tests;

/// <summary>
/// 图像格式识别的唯一来源：文件系统读图、CodeAct、MCP 都读这里。
/// </summary>
public class ImageMimeDetectorTests
{
    private static readonly byte[] PngHeader =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    [Fact]
    public void Detect_RecognizesSupportedFormatsFromMagicBytes()
    {
        ImageMimeDetector.Detect(PngHeader).Should().Be("image/png");
        ImageMimeDetector.Detect([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be("image/jpeg");
        ImageMimeDetector.Detect([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]).Should().Be("image/gif");
        ImageMimeDetector.Detect([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50])
            .Should().Be("image/webp");
    }

    [Fact]
    public void Detect_RejectsUnknownOrTruncatedData()
    {
        ImageMimeDetector.Detect([]).Should().BeNull();
        ImageMimeDetector.Detect([0x42, 0x4D, 0x00]).Should().BeNull();
        ImageMimeDetector.Detect([0x47, 0x49, 0x46, 0x38, 0x38, 0x61]).Should().BeNull();
        ImageMimeDetector.Detect([0x89, 0x50, 0x4E, 0x47]).Should().BeNull();
    }

    [Fact]
    public void TryResolve_DerivesMimeTypeFromBytesWhenDeclarationIsMissing()
    {
        ImageMimeDetector.TryResolve(null, PngHeader, out var mimeType, out var error).Should().BeTrue();

        mimeType.Should().Be("image/png");
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("IMAGE/PNG")]
    [InlineData(" image/png ")]
    public void TryResolve_AcceptsMatchingDeclarationCaseInsensitively(string declared)
    {
        ImageMimeDetector.TryResolve(declared, PngHeader, out var mimeType, out var error).Should().BeTrue();

        mimeType.Should().Be("image/png");
        error.Should().BeNull();
    }

    [Fact]
    public void TryResolve_RejectsMismatchedDeclarationInsteadOfFixingIt()
    {
        ImageMimeDetector.TryResolve("image/jpeg", PngHeader, out var mimeType, out var error).Should().BeFalse();

        mimeType.Should().BeEmpty();
        error.Should().Contain("does not match");
        error.Should().Contain("image/png");
    }

    [Fact]
    public void TryResolve_RejectsUnsupportedDeclaredType()
    {
        ImageMimeDetector.TryResolve("image/svg+xml", PngHeader, out _, out var error).Should().BeFalse();

        error.Should().Contain("image/svg+xml");
        error.Should().Contain("supported");
    }

    [Fact]
    public void TryResolve_RejectsUnrecognizedOrEmptyData()
    {
        ImageMimeDetector.TryResolve("image/png", [], out _, out var emptyError).Should().BeFalse();
        emptyError.Should().Contain("empty");

        ImageMimeDetector.TryResolve("image/png", "plain text"u8.ToArray(), out _, out var unknownError)
            .Should().BeFalse();
        unknownError.Should().Contain("unrecognized");
    }
}
