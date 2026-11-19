using ShimmerChat.Singletons;
using SkiaSharp;
using ShimmerChatLib;

namespace ShimmerChat.Tests;

/// <summary>
/// 图像规范化器：像素预算缩放、算法重编码、GIF 跳过与解码失败的显式语义。
/// </summary>
public class ImageNormalizerTests
{
	private static ImageProcessingOptions Options(
		long? budget = null, ImageCompressionAlgorithm algorithm = ImageCompressionAlgorithm.WebP, int quality = 85)
		=> new()
		{
			PixelBudget = budget ?? ImageAttachmentRules.DefaultPixelBudget,
			Algorithm = algorithm,
			Quality = quality
		};

	[Fact]
	public void ImageWithinBudget_ReturnsBytesUntouched()
	{
		// 预算内的图像必须零质量损失：原字节、原 MIME、不改写
		var data = TestImageFactory.RenderPng(64, 64);

		var result = ImageNormalizer.Process(data, "image/png", Options());

		result.Changed.Should().BeFalse();
		result.MimeType.Should().Be("image/png");
		result.Data.Should().BeSameAs(data);
	}

	[Fact]
	public void ImageOverBudget_IsScaledIntoTheBudgetAndReencoded()
	{
		var data = TestImageFactory.RenderNoisePng(1500, 1000); // 1.5M 像素

		var result = ImageNormalizer.Process(data, "image/png", Options(budget: 512L * 512));

		result.Changed.Should().BeTrue();
		result.MimeType.Should().Be("image/webp");

		var (width, height, format) = TestImageFactory.Probe(result.Data);
		width.Should().Be(627);
		height.Should().Be(418);
		((long)width * height).Should().BeLessOrEqualTo(512L * 512);
		format.Should().Be(SKEncodedImageFormat.Webp);
		result.Data.Length.Should().BeLessThan(data.Length);
	}

	[Fact]
	public void GifIsNeverTouched_EvenWhenOverBudget()
	{
		// GIF 通常是动画：重编码只保留第一帧，等于悄悄丢内容
		var data = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };

		var result = ImageNormalizer.Process(data, "image/gif", Options(budget: 1));

		result.Changed.Should().BeFalse();
		result.Data.Should().BeSameAs(data);
		result.MimeType.Should().Be("image/gif");
	}

	[Fact]
	public void CorruptData_ThrowsExplicitErrorInsteadOfReturningTheOriginal()
	{
		var act = () => ImageNormalizer.Process(
			new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, "image/png", Options());

		act.Should().Throw<InvalidDataException>()
			.WithMessage("*cannot be decoded*");
	}

	[Fact]
	public void JpegAlgorithm_FlattensTransparencyAndOutputsJpeg()
	{
		var data = TestImageFactory.RenderPng(800, 800, withAlpha: true);

		var result = ImageNormalizer.Process(
			data, "image/png", Options(budget: 512L * 512, algorithm: ImageCompressionAlgorithm.Jpeg));

		result.Changed.Should().BeTrue();
		result.MimeType.Should().Be("image/jpeg");

		// 透明区域必须合成白底，而不是编码后变黑
		var (width, height, format) = TestImageFactory.Probe(result.Data);
		format.Should().Be(SKEncodedImageFormat.Jpeg);
		((long)width * height).Should().BeLessOrEqualTo(512L * 512);

		using var decoded = SKBitmap.Decode(result.Data);
		var corner = decoded.GetPixel(0, 0);
		corner.Red.Should().BeGreaterThan(240, "fully transparent corner must flatten to white");
		corner.Green.Should().BeGreaterThan(240);
		corner.Blue.Should().BeGreaterThan(240);
	}

	[Fact]
	public void NoneAlgorithm_KeepsTheOriginalFormat()
	{
		var data = TestImageFactory.RenderNoisePng(1500, 1000);

		var result = ImageNormalizer.Process(
			data, "image/png", Options(budget: 512L * 512, algorithm: ImageCompressionAlgorithm.None));

		result.Changed.Should().BeTrue();
		result.MimeType.Should().Be("image/png");

		var (width, height, format) = TestImageFactory.Probe(result.Data);
		format.Should().Be(SKEncodedImageFormat.Png);
		((long)width * height).Should().BeLessOrEqualTo(512L * 512);
	}

	[Fact]
	public void PixelCeiling_RejectsWithExplicitErrorInsteadOfDecoding()
	{
		// 解码两万像素见方的图像会占用 GB 级内存：必须在解码前显式拒绝
		var data = BuildMinimalPng(20_000, 20_000);

		var act = () => ImageNormalizer.Process(data, "image/png", Options());

		act.Should().Throw<InvalidDataException>()
			.WithMessage("*beyond*pixel processing ceiling*");
	}

	/// <summary>
	/// 构造只声明尺寸、像素数据为空流的最小 PNG：SKCodec 只读头部即可拿到声明尺寸，
	/// 而无须真实解码 20k × 20k 的像素数据。
	/// </summary>
	private static byte[] BuildMinimalPng(int width, int height)
	{
		var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		var ihdrData = new byte[13];
		WriteBigEndian(ihdrData, 0, (uint)width);
		WriteBigEndian(ihdrData, 4, (uint)height);
		ihdrData[8] = 8;  // bit depth
		ihdrData[9] = 2;  // color type: RGB
		ihdrData[10] = 0; // compression
		ihdrData[11] = 0; // filter
		ihdrData[12] = 0; // interlace

		var ihdr = Chunk("IHDR"u8, ihdrData);
		// 空的 zlib 流：头部 78 01 + 空 deflate 块 03 00 + adler32(空)=1
		var idat = Chunk("IDAT"u8, (ReadOnlySpan<byte>)[0x78, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
		var iend = Chunk("IEND"u8, ReadOnlySpan<byte>.Empty);

		return [.. signature, .. ihdr, .. idat, .. iend];
	}

	/// <summary>length(4) + type(4) + data + CRC(type+data)，与 PNG 规范一致。</summary>
	private static byte[] Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		var chunk = new byte[4 + 4 + data.Length + 4];
		WriteBigEndian(chunk, 0, (uint)data.Length);
		type.CopyTo(chunk.AsSpan(4));
		data.CopyTo(chunk.AsSpan(8));
		WriteBigEndian(chunk, 4 + 4 + data.Length, Crc32(chunk, 4, 4 + data.Length));
		return chunk;
	}

	private static void WriteBigEndian(byte[] target, int offset, uint value)
	{
		target[offset] = (byte)(value >> 24);
		target[offset + 1] = (byte)(value >> 16);
		target[offset + 2] = (byte)(value >> 8);
		target[offset + 3] = (byte)value;
	}

	private static uint Crc32(byte[] data, int offset, int length)
	{
		uint crc = 0xFFFFFFFF;
		for (var i = 0; i < length; i++)
		{
			crc ^= data[offset + i];
			for (var bit = 0; bit < 8; bit++)
			{
				// uint 算术保证环绕语义：最低位为 1 时与多项式异或
				var mask = (crc & 1) * 0xEDB88320u;
				crc = (crc >> 1) ^ mask;
			}
		}
		return crc ^ 0xFFFFFFFF;
	}
}
