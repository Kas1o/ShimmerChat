using SkiaSharp;

namespace ShimmerChat.Tests;

/// <summary>
/// 测试图像生成：产出真实可解码的 PNG 字节（带渐变 / 噪声内容，让压缩与体积断言有区分度），
/// 以及只读头部的尺寸探测。
/// </summary>
public static class TestImageFactory
{
	/// <summary>生成一张 RGBA 渐变 PNG；withAlpha 时带从 0 渐变到 255 的透明度。</summary>
	public static byte[] RenderPng(int width, int height, bool withAlpha = false)
	{
		using var bitmap = new SKBitmap(
			new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		var span = bitmap.GetPixelSpan();
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var offset = (y * width + x) * 4;
				span[offset] = (byte)x;
				span[offset + 1] = (byte)y;
				span[offset + 2] = (byte)(x + y);
				span[offset + 3] = withAlpha
					? (byte)(x * 255 / Math.Max(1, width - 1))
					: (byte)255;
			}
		}

		using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
		return data!.ToArray();
	}

	/// <summary>
	/// 生成一张不可压缩的噪声 PNG：用于「处理后体积必须下降」这类断言。
	/// 平坦渐变图会被 PNG 无损压缩压得极小，不能代表真实照片。
	/// </summary>
	public static byte[] RenderNoisePng(int width, int height, int seed = 12345)
	{
		var random = new Random(seed);
		using var bitmap = new SKBitmap(
			new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		var span = bitmap.GetPixelSpan();
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var offset = (y * width + x) * 4;
				span[offset] = (byte)random.Next(256);
				span[offset + 1] = (byte)random.Next(256);
				span[offset + 2] = (byte)random.Next(256);
				span[offset + 3] = 255;
			}
		}

		using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
		return data!.ToArray();
	}

	/// <summary>只读图像头部：尺寸与编码格式，不解码像素。探测失败时抛异常，测试随断言失败。</summary>
	public static (int Width, int Height, SKEncodedImageFormat Format) Probe(byte[] data)
	{
		using var codecData = SKData.CreateCopy(data);
		var codec = SKCodec.Create(codecData);
		codec.Should().NotBeNull("processed image data must stay a decodable image");
		using var _ = codec!;
		return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
	}
}
