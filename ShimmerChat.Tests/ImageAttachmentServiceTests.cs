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
        => CreateService(storedMode, processingMode: null);

    /// <summary>
    /// 构建带上传处理设置的测试服务。KV 未显式 setup 的键由 Moq 返回 null，
    /// 与「设置缺失 → 原图模式 / 默认参数」的读取语义一致。
    /// </summary>
    private static ImageAttachmentService CreateService(
        string? storedMode,
        string? processingMode,
        long? pixelBudget = null,
        string? compression = null,
        int? quality = null)
    {
        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.StorageModeKey))
            .Returns(storedMode);
        kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.UploadProcessingModeKey))
            .Returns(processingMode);
        if (pixelBudget != null)
            kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.UploadPixelBudgetKey))
                .Returns(pixelBudget.ToString());
        if (compression != null)
            kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.UploadCompressionKey))
                .Returns(compression);
        if (quality != null)
            kv.Setup(k => k.Read(ImageAttachmentService.SettingsSpace, ImageAttachmentService.UploadQualityKey))
                .Returns(quality.ToString());
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
    public async Task SaveGeneratedAsync_AlwaysWritesFileEvenInBase64Mode()
    {
        // 生成结果需要稳定的 /userimages/ 链接，不能跟随「上传图像内联」设置变成 data URL
        var service = CreateService(nameof(ImageStorageMode.MessageBase64));

        var image = await service.SaveGeneratedAsync(PngStream(), "image/png");

        try
        {
            image.Base64.Should().BeNull();
            image.FileName.Should().EndWith(".png");
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

    // ── 上传处理模式（原图 / 处理）──────────────────────────────

    [Fact]
    public void UploadProcessing_DefaultsToOriginalWhenUnset()
    {
        CreateService(nameof(ImageStorageMode.UserUploadImage)).UploadProcessing.Should().BeNull();
    }

    [Fact]
    public void UploadProcessing_FallsBackToOriginalWhenStoredModeIsUnknown()
    {
        // 坏配置不能让上传挂掉，但也必须留下错误日志（由实现负责）
        CreateService(nameof(ImageStorageMode.UserUploadImage), "NotAMode").UploadProcessing
            .Should().BeNull("未知的处理模式必须回退为原图模式");
    }

    [Fact]
    public void UploadProcessing_ProcessedMode_ReadsParameters()
    {
        var processing = CreateService(
            nameof(ImageStorageMode.UserUploadImage),
            nameof(ImageUploadProcessingMode.Processed),
            pixelBudget: 1024L * 1024,
            compression: nameof(ImageCompressionAlgorithm.Jpeg),
            quality: 70).UploadProcessing;

        processing.Should().NotBeNull();
        processing!.PixelBudget.Should().Be(1024L * 1024);
        processing.Algorithm.Should().Be(ImageCompressionAlgorithm.Jpeg);
        processing.Quality.Should().Be(70);
    }

    [Fact]
    public void UploadProcessing_ProcessedMode_ConvergesInvalidParametersToDefaults()
    {
        // 单个参数坏掉只影响该参数：其余显式配置不被连带重置
        var processing = CreateService(
            nameof(ImageStorageMode.UserUploadImage),
            nameof(ImageUploadProcessingMode.Processed),
            pixelBudget: null,
            compression: "Nope",
            quality: null).UploadProcessing;

        processing.Should().NotBeNull();
        processing!.PixelBudget.Should().Be(ImageAttachmentRules.DefaultPixelBudget);
        processing.Algorithm.Should().Be(ImageCompressionAlgorithm.WebP);
        processing.Quality.Should().Be(ImageAttachmentRules.DefaultCompressionQuality);
    }

    [Fact]
    public async Task SaveAsync_ProcessedMode_StoresTheProcessedImage()
    {
        // 处理模式的落盘结果必须满足预算，否则「上传即处理」就是空话
        var bytes = TestImageFactory.RenderNoisePng(1500, 1000);
        var service = CreateService(
            nameof(ImageStorageMode.UserUploadImage),
            nameof(ImageUploadProcessingMode.Processed),
            pixelBudget: 512L * 512);

        var image = await service.SaveAsync(new MemoryStream(bytes), "image/png");

        try
        {
            image.MimeType.Should().Be("image/webp");
            image.FileName.Should().EndWith(".webp");

            var stored = File.ReadAllBytes(PathOf(image));
            stored.Length.Should().BeLessThan(bytes.Length);
            var (width, height, format) = TestImageFactory.Probe(stored);
            ((long)width * height).Should().BeLessOrEqualTo(512L * 512);
            format.Should().Be(SkiaSharp.SKEncodedImageFormat.Webp);
        }
        finally
        {
            File.Delete(PathOf(image));
        }
    }

    [Fact]
    public async Task SaveAsync_ProcessedMode_KeepsSmallImagesUntouched()
    {
        var bytes = TestImageFactory.RenderPng(64, 64);
        var service = CreateService(
            nameof(ImageStorageMode.UserUploadImage),
            nameof(ImageUploadProcessingMode.Processed));

        var image = await service.SaveAsync(new MemoryStream(bytes), "image/png");

        try
        {
            image.MimeType.Should().Be("image/png");
            File.ReadAllBytes(PathOf(image)).Should().Equal(bytes);
        }
        finally
        {
            File.Delete(PathOf(image));
        }
    }

    [Fact]
    public async Task SaveAsync_ProcessedMode_RejectsUndecodableData()
    {
        var service = CreateService(
            nameof(ImageStorageMode.UserUploadImage),
            nameof(ImageUploadProcessingMode.Processed));

        var act = async () => await service.SaveAsync(PngStream(), "image/png");

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    // ── 请求侧预算处理（ToChatImageAsync 重载）──────────────────

    [Fact]
    public async Task ToChatImageAsync_WithBudget_ProcessesOverBudgetStoredFile()
    {
        // 工具生成图始终原图落盘；它的请求体积由这里兜底，且消息与文件都不被改写
        var bytes = TestImageFactory.RenderNoisePng(1500, 1000);
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));
        var image = await service.SaveGeneratedAsync(new MemoryStream(bytes), "image/png");

        try
        {
            var request = await service.ToChatImageAsync(
                image,
                new ImageProcessingOptions { PixelBudget = 512L * 512, Algorithm = ImageCompressionAlgorithm.WebP });

            request.MimeType.Should().Be("image/webp");
            var decoded = Convert.FromBase64String(request.Base64);
            var (width, height, _) = TestImageFactory.Probe(decoded);
            ((long)width * height).Should().BeLessOrEqualTo(512L * 512);

            // 消息引用的文件保持原图：预算处理只影响请求副本
            File.ReadAllBytes(PathOf(image)).Should().Equal(bytes);
        }
        finally
        {
            File.Delete(PathOf(image));
        }
    }

    [Fact]
    public async Task ToChatImageAsync_WithBudget_ProcessesInlineBase64Attachment()
    {
        var bytes = TestImageFactory.RenderNoisePng(1500, 1000);
        var image = MessageImage.FromBase64(Convert.ToBase64String(bytes), "image/png");
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var request = await service.ToChatImageAsync(
            image,
            new ImageProcessingOptions { PixelBudget = 512L * 512, Algorithm = ImageCompressionAlgorithm.WebP });

        request.MimeType.Should().Be("image/webp");
        var decoded = Convert.FromBase64String(request.Base64);
        var (width, height, _) = TestImageFactory.Probe(decoded);
        ((long)width * height).Should().BeLessOrEqualTo(512L * 512);

        // 内联附件同样只改请求副本，消息里的 Base64 原样保留
        image.Base64.Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public async Task ToChatImageAsync_WithNullBudget_ResolvesBytesAsStored()
    {
        var bytes = TestImageFactory.RenderPng(1500, 1000);
        var image = MessageImage.FromBase64(Convert.ToBase64String(bytes), "image/png");
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var request = await service.ToChatImageAsync(image, processing: null);

        request.MimeType.Should().Be("image/png");
        request.Base64.Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public async Task ToChatImageAsync_WithBudget_RejectsUndecodableData()
    {
        var image = MessageImage.FromBase64(Convert.ToBase64String(PngBytes), "image/png");
        var service = CreateService(nameof(ImageStorageMode.UserUploadImage));

        var act = async () => await service.ToChatImageAsync(
            image, new ImageProcessingOptions { PixelBudget = 512L * 512 });

        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
