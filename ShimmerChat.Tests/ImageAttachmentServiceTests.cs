using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Singletons;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 用户上传图像服务：按设置选择 UserUploadImage 目录（只存文件名）或消息内联 Base64，
/// 并在构建请求前把附件解析为请求可用的 Base64。
/// </summary>
public class ImageAttachmentServiceTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47];

    private static ImageAttachmentService CreateService(string? storedMode)
    {
        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.StorageModeKey))
            .Returns(storedMode);
        return new ImageAttachmentService(kv.Object, NullLogger<ImageAttachmentService>.Instance);
    }

    private static MemoryStream PngStream() => new(PngBytes);

    private static string PathOf(MessageImage image) =>
        Path.Combine(ImageAttachmentService.UploadDirectory, image.FileName!);

    [Fact]
    public void Mode_DefaultsToUserUploadImageWhenUnset()
    {
        CreateService(null).Mode.Should().Be(ImageStorageMode.UserUploadImage);
    }

    [Fact]
    public void Mode_FallsBackToDefaultWhenStoredValueIsUnknown()
    {
        // 坏配置不能让界面挂掉，但也必须留下错误日志（由实现负责）
        CreateService("NotAMode").Mode.Should().Be(ImageStorageMode.UserUploadImage);
    }

    [Fact]
    public void Mode_ReadsConfiguredValue()
    {
        CreateService(nameof(ImageStorageMode.MessageBase64)).Mode.Should().Be(ImageStorageMode.MessageBase64);
    }

    [Fact]
    public async Task SaveAsync_UserUploadImageMode_KeepsOnlyAFileNameReference()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var image = await service.SaveAsync(PngStream(), "image/png");

        try
        {
            image.Base64.Should().BeNull();
            image.MimeType.Should().Be("image/png");
            image.FileName.Should().EndWith(".png");
            image.FileName.Should().Be(Path.GetFileName(image.FileName!), "消息里只保存文件名，不含路径");
            File.Exists(PathOf(image)).Should().BeTrue();
            File.ReadAllBytes(PathOf(image)).Should().Equal(PngBytes);

            service.GetDisplayUrl(image).Should().Be($"/userimages/{image.FileName}");
        }
        finally
        {
            File.Delete(PathOf(image));
        }
    }

    [Fact]
    public async Task SaveAsync_MessageBase64Mode_EmbedsDataWithoutTouchingDisk()
    {
        var service = CreateService(nameof(ImageStorageMode.MessageBase64));

        var image = await service.SaveAsync(PngStream(), "image/jpeg; charset=binary");

        image.FileName.Should().BeNull();
        image.Base64.Should().Be(Convert.ToBase64String(PngBytes));
        image.MimeType.Should().Be("image/jpeg");
        service.GetDisplayUrl(image).Should().Be($"data:image/jpeg;base64,{Convert.ToBase64String(PngBytes)}");
    }

    [Fact]
    public async Task ToChatImageAsync_ResolvesFileNameIntoBase64()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = await service.SaveAsync(PngStream(), "image/png");

        try
        {
            var requestImage = await service.ToChatImageAsync(image);

            requestImage.MimeType.Should().Be("image/png");
            requestImage.Base64.Should().Be(Convert.ToBase64String(PngBytes));
            // 解析只读文件，附件本身保持「只有文件名」的形态
            image.Base64.Should().BeNull();
        }
        finally
        {
            File.Delete(PathOf(image));
        }
    }

    [Fact]
    public async Task ToChatImageAsync_RejectsMissingFileWithExplicitError()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = MessageImage.FromFile("definitely-missing.png", "image/png");

        var act = async () => await service.ToChatImageAsync(image);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ToChatImageAsync_RejectsFileNameThatEscapesTheUploadDirectory()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = MessageImage.FromFile("../../appsettings.json", "image/png");

        var act = async () => await service.ToChatImageAsync(image);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ToChatImageAsync_RejectsAttachmentWithoutAnyData()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = new MessageImage { MimeType = "image/png" };

        var act = async () => await service.ToChatImageAsync(image);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteStoredFile_RemovesFileAndToleratesMissingOnes()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = await service.SaveAsync(PngStream(), "image/png");
        var missing = MessageImage.FromFile("not-there.png", "image/png");

        service.DeleteStoredFile(image);
        File.Exists(PathOf(image)).Should().BeFalse();

        var act = () => service.DeleteStoredFile(missing);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SaveAsync_RejectsUnsupportedContentType()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var act = async () => await service.SaveAsync(PngStream(), "application/pdf");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyUpload()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var act = async () => await service.SaveAsync(new MemoryStream(), "image/png");

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void GetDisplayUrl_ReturnsNullWhenReferencedFileIsGone()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        service.GetDisplayUrl(MessageImage.FromFile("missing.png", "image/png")).Should().BeNull();
    }

    [Fact]
    public void GetDisplayUrl_ReturnsNullForMalformedFileName()
    {
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        service.GetDisplayUrl(MessageImage.FromFile("../secret.png", "image/png")).Should().BeNull();
    }
}
