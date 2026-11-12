using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 图像附件规则的唯一来源：UI 的 accept、上传校验与应用层保存共用同一份 MIME 列表。
/// </summary>
public class ImageAttachmentRulesTests
{
    [Fact]
    public void SupportedMimeTypes_CoverExactlyTheMimeToExtensionKeys()
    {
        ImageAttachmentRules.SupportedMimeTypes
            .Should().BeEquivalentTo(ImageAttachmentRules.MimeToExtension.Keys);

        ImageAttachmentRules.Accept
            .Split(',')
            .Should().BeEquivalentTo(ImageAttachmentRules.SupportedMimeTypes);
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/JPEG", true)]
    [InlineData(" image/webp ", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedMimeType_IsCaseInsensitiveAndRejectsUnknownTypes(string? mimeType, bool expected)
    {
        ImageAttachmentRules.IsSupportedMimeType(mimeType).Should().Be(expected);
    }

    [Fact]
    public void TryParseDataUrl_ExtractsMimeAndPayload()
    {
        var parsed = ImageAttachmentRules.TryParseDataUrl(
            "data:image/webp;base64,QUJD", out var base64, out var mimeType);

        parsed.Should().BeTrue();
        base64.Should().Be("QUJD");
        mimeType.Should().Be("image/webp");
    }

    [Fact]
    public void TryParseDataUrl_DefaultsToPngWhenMimeIsMissing()
    {
        var parsed = ImageAttachmentRules.TryParseDataUrl(
            "data:;base64,QUJD", out var base64, out var mimeType);

        parsed.Should().BeTrue();
        base64.Should().Be("QUJD");
        mimeType.Should().Be(ChatImage.DefaultMimeType);
    }

    [Fact]
    public void TryParseDataUrl_LeavesRawBase64Alone()
    {
        var parsed = ImageAttachmentRules.TryParseDataUrl("QUJD", out var base64, out var mimeType);

        parsed.Should().BeFalse();
        base64.Should().BeEmpty();
        mimeType.Should().Be(ChatImage.DefaultMimeType);
    }
}
