using ModelContextProtocol.Protocol;
using ShimmerChatBuiltin.Mcp;
using ShimmerChatBuiltin.Tests.ImageGeneration;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Mcp;

public class McpContentFormatterTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static McpContentFormatter Formatter() => new("filesystem", null);

    private static CallToolResult ToolResultWith(params ContentBlock[] blocks) =>
        new() { Content = blocks };

    [Fact]
    public void FormatToolResult_AttachesSupportedImage()
    {
        var result = ToolResultWith(
            new TextContentBlock { Text = "chart rendered" },
            ImageContentBlock.FromBytes(PngBytes, "image/png"));

        var content = Formatter().FormatToolResult(result, "render_chart");

        content.Text.Should().Contain("chart rendered");
        content.Text.Should().Contain("attached to this tool result");
        content.Images.Should().ContainSingle();
        content.Images[0].MimeType.Should().Be("image/png");
        content.Images[0].Data.Should().Equal(PngBytes);
    }

    [Fact]
    public void FormatToolResult_DescribeOnly_OmitsWithoutCollecting()
    {
        var result = ToolResultWith(ImageContentBlock.FromBytes(PngBytes, "image/png"));

        var content = Formatter().FormatToolResult(result, "render_chart", McpImageOutput.DescribeOnly);

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("image/png");
        content.Text.Should().Contain("not injected into context");
        content.Text.Should().NotContain("attached");
    }

    [Fact]
    public void FormatToolResult_DeclaredMimeMismatch_AnnotatesAndSkipsImage()
    {
        // 服务端把 JPEG 声明成 PNG：不静默改写，标注原因后跳过。
        var result = ToolResultWith(ImageContentBlock.FromBytes(PngBytes, "image/jpeg"));

        var content = Formatter().FormatToolResult(result, "render_chart");

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("does not match");
        content.Text.Should().Contain("image/png");
    }

    [Fact]
    public void FormatToolResult_UnsupportedDeclaredType_AnnotatesAndSkipsImage()
    {
        var result = ToolResultWith(ImageContentBlock.FromBytes(PngBytes, "image/svg+xml"));

        var content = Formatter().FormatToolResult(result, "render_chart");

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("unsupported image MIME type");
        content.Text.Should().Contain("image/svg+xml");
    }

    [Fact]
    public void FormatToolResult_OversizedImage_AnnotatesLimitAndSkipsImage()
    {
        var oversized = new byte[(int)ImageAttachmentRules.MaxBytes + 1];
        PngBytes.CopyTo(oversized, 0);
        var result = ToolResultWith(ImageContentBlock.FromBytes(oversized, "image/png"));

        var content = Formatter().FormatToolResult(result, "render_chart");

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("larger than the");
        content.Text.Should().Contain(ImageAttachmentRules.MaxBytes.ToString());
    }

    [Fact]
    public void FormatToolResult_KeepsTextAlongsideImages()
    {
        var result = ToolResultWith(
            new TextContentBlock { Text = "see image" },
            ImageContentBlock.FromBytes(PngBytes, "image/png"));

        var content = Formatter().FormatToolResult(result, "render_chart");

        content.Text.Should().Contain("see image");
        content.Images.Should().ContainSingle();
    }

    [Fact]
    public void FormatToolResult_NoContent_StillReportsExplicitly()
    {
        var content = Formatter().FormatToolResult(new CallToolResult(), "render_chart");

        content.Text.Should().Contain("returned no content");
        content.Images.Should().BeEmpty();
    }

    [Fact]
    public void FormatResourceResult_AttachesImageBlobWhenAsked()
    {
        var blob = BlobResourceContents.FromBytes(PngBytes, "file:///chart.png", "image/png");
        var result = new ReadResourceResult { Contents = [blob] };

        var content = Formatter().FormatResourceResult(result, "file:///chart.png", McpImageOutput.Attach);

        content.Images.Should().ContainSingle();
        content.Images[0].MimeType.Should().Be("image/png");
        content.Images[0].Data.Should().Equal(PngBytes);
    }

    [Fact]
    public void FormatResourceResult_ImageBlobIsOmittedByDefault()
    {
        var blob = BlobResourceContents.FromBytes(PngBytes, "file:///chart.png", "image/png");
        var result = new ReadResourceResult { Contents = [blob] };

        var content = Formatter().FormatResourceResult(result, "file:///chart.png");

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("not injected into context");
    }

    [Fact]
    public void FormatResourceResult_NonImageBlobIsAnnotatedWithoutDecodingAsImage()
    {
        var blob = BlobResourceContents.FromBytes(
            "PK\u0003\u0004 fake zip"u8.ToArray(), "file:///a.zip", "application/zip");
        var result = new ReadResourceResult { Contents = [blob] };

        var content = Formatter().FormatResourceResult(result, "file:///a.zip", McpImageOutput.Attach);

        content.Images.Should().BeEmpty();
        content.Text.Should().Contain("binary resource");
        content.Text.Should().Contain("application/zip");
        content.Text.Should().Contain("binary content is not injected into context");
    }
}

public class McpToolResultImagesTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static McpContentResult ContentWithImage() =>
        new("chart", [new McpImageData("image/png", PngBytes)]);

    [Fact]
    public async Task ToToolResultAsync_SavesImagesAndAttachesThem()
    {
        var attachments = new FakeImageAttachmentService();

        var result = await McpToolResultImages.ToToolResultAsync(
            ContentWithImage(), "tool 'render_chart'", attachments, null, CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("chart");
        result.Images.Should().ContainSingle();
        result.Images![0].FileName.Should().NotBeNull();
        attachments.Saved.Should().ContainSingle();
        attachments.Saved[0].ContentType.Should().Be("image/png");
        attachments.Saved[0].Content.Should().Equal(PngBytes);
    }

    [Fact]
    public async Task ToToolResultAsync_NoImages_ReturnsTextOnly()
    {
        var attachments = new FakeImageAttachmentService();

        var result = await McpToolResultImages.ToToolResultAsync(
            McpContentResult.TextOnly("plain"), "tool 'x'", attachments, null, CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Content.Should().Be("plain");
        result.Images.Should().BeNull();
        attachments.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ToToolResultAsync_SaveFailure_ReturnsExplicitOutputWriteFailure()
    {
        var attachments = new FakeImageAttachmentService { ThrowOnSave = true };

        var result = await McpToolResultImages.ToToolResultAsync(
            ContentWithImage(), "tool 'render_chart'", attachments, null, CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.OutputWriteFailed);
        result.ErrorMessage.Should().Contain("render_chart");
    }

    [Fact]
    public async Task ToToolResultAsync_MissingStorage_FailsExplicitly()
    {
        var result = await McpToolResultImages.ToToolResultAsync(
            ContentWithImage(), "tool 'render_chart'", null, null, CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.ConfigInvalid);
        result.ErrorMessage.Should().Contain("image storage is unavailable");
    }
}
