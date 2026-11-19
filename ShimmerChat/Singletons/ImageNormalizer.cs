using SkiaSharp;
using ShimmerChatLib;

namespace ShimmerChat.Singletons
{
	/// <summary>
	/// 图像规范化器：把像素总量超过预算的图像等比缩放到预算内，再按所选算法重编码。
	/// <para>
	/// 这是唯一的像素级处理实现——上传存储的「处理模式」与加载对话消息节点的
	/// 「预算处理」都经过这里，两处语义不会各自漂移。
	/// </para>
	/// <para>
	/// 基于 SkiaSharp（Chrome / Android 同源的解码器）。native 运行库缺失时异常原样向上抛出
	/// （<see cref="DllNotFoundException"/> 等），不把它伪装成图像数据错误。
	/// </para>
	/// </summary>
	/// <remarks>
	/// 不超预算的图像按原字节原样返回，不做任何重编码（零质量损失）。
	/// 数据无法解码、或像素总量超过解码安全上限时显式抛 <see cref="InvalidDataException"/>，
	/// 绝不静默跳过或退回原图掩盖失败。
	/// </remarks>
	public static class ImageNormalizer
	{
		/// <summary>一次处理的结果：目标数据、目标 MIME、是否发生了修改。</summary>
		public sealed record Result(byte[] Data, string MimeType, bool Changed);

		/// <summary>
		/// 解码器愿意处理的最大像素总量。超过它的图像（例如手工构造的两万像素见方 PNG）
		/// 全量解码会占用数百 MB 内存，直接显式拒绝处理，而不是冒险解码。
		/// </summary>
		public const long DecodablePixelCeiling = ImageAttachmentRules.MaxPixelBudget * 4;

		/// <summary>
		/// 按参数处理一张图像：超像素预算则等比缩放并按算法重编码，否则原样返回。
		/// 解码与重编码都在内存中同步完成；20MiB 的上传上限决定了它只适合在
		/// 后台生成任务中调用，不得占用渲染线程。
		/// </summary>
		public static Result Process(byte[] data, string mimeType, ImageProcessingOptions options)
		{
			if (data is null || data.Length == 0)
				throw new InvalidDataException("Image data is empty; nothing to process.");

			// GIF 通常是多帧动画：重编码只保留第一帧，等于悄悄丢内容，因此永远原样返回。
			if (string.Equals(mimeType, "image/gif", StringComparison.OrdinalIgnoreCase))
				return new Result(data, mimeType, false);

			// 只读头部探测：不超预算 / 多帧动画的图像不必付出解码成本。
			using var codecData = SKData.CreateCopy(data);
			using var codec = SKCodec.Create(codecData);
			if (codec == null)
				throw new InvalidDataException(
					$"Image cannot be decoded, so the pixel budget cannot be applied ({mimeType}): " +
					"the encoded format is not recognized.");

			// 多帧动画（动画 WebP 等）与 GIF 同理：重编码等于丢帧，原样返回。
			if (codec.FrameCount > 1)
				return new Result(data, mimeType, false);

			var pixels = (long)codec.Info.Width * codec.Info.Height;
			if (pixels <= options.PixelBudget)
				return new Result(data, mimeType, false);

			if (pixels > DecodablePixelCeiling)
				throw new InvalidDataException(
					$"Image is {codec.Info.Width}x{codec.Info.Height} ({pixels} pixels), beyond the " +
					$"{DecodablePixelCeiling} pixel processing ceiling. Downscale it externally or store it as original.");

			// 解码 → 展开手机照片的 EXIF 方向 → 等比缩放到预算内 → 重编码。
			using var source = SKBitmap.Decode(codecData);
			if (source == null)
				throw new InvalidDataException(
					$"Image cannot be decoded, so the pixel budget cannot be applied ({mimeType}): " +
					"the pixel data is corrupt.");

			// 返回 null 表示方向已是默认（TopLeft），直接用原图，避免无谓的整帧重画。
			using var orientedExtra = ApplyEncodedOrientation(source, codec.EncodedOrigin);
			var oriented = orientedExtra ?? source;

			var scale = Math.Sqrt((double)options.PixelBudget / pixels);
			var width = Math.Max(1, (int)Math.Floor(oriented.Width * scale));
			var height = Math.Max(1, (int)Math.Floor(oriented.Height * scale));
			using var scaled = oriented.Resize(
				new SKImageInfo(width, height),
				new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

			// JPEG 没有透明通道：有透明像素的图像先合成白底，避免透明区域编码后变黑。
			// 对不含透明的图像该操作是空操作。
			var quality = ImageAttachmentRules.ClampCompressionQuality(options.Quality);
			var (encoded, outputMime) = options.Algorithm switch
			{
				ImageCompressionAlgorithm.WebP => (Encode(scaled, SKEncodedImageFormat.Webp, quality), "image/webp"),
				ImageCompressionAlgorithm.Jpeg => (Encode(FlattenToWhite(scaled), SKEncodedImageFormat.Jpeg, quality), "image/jpeg"),
				ImageCompressionAlgorithm.None => (EncodeOriginalFormat(scaled, mimeType, quality), mimeType),
				_ => throw new ArgumentOutOfRangeException(
					nameof(options), options.Algorithm, "Unknown image compression algorithm.")
			};

			return new Result(encoded, outputMime, true);
		}

		/// <summary>编码到目标格式；失败（如该格式的编码器不可用）显式抛错，不吞。</summary>
		private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
		{
			using var encoded = bitmap.Encode(format, quality);
			if (encoded == null)
				throw new InvalidDataException($"Failed to encode the processed image as {format}.");

			return encoded.ToArray();
		}

		/// <summary>
		/// 「仅缩放不转格式」：按原 MIME 选择编码器。JPEG / WebP 重编码沿用质量参数
		/// （它们本来就有损）；PNG 保持无损；GIF / 动画在此之前已被跳过，走到这里属于数据错误。
		/// </summary>
		private static byte[] EncodeOriginalFormat(SKBitmap bitmap, string mimeType, int quality) => mimeType switch
		{
			"image/png" => Encode(bitmap, SKEncodedImageFormat.Png, 100),
			"image/jpeg" => Encode(bitmap, SKEncodedImageFormat.Jpeg, quality),
			"image/webp" => Encode(bitmap, SKEncodedImageFormat.Webp, quality),
			_ => throw new InvalidDataException(
				$"Cannot re-encode '{mimeType}' without converting the format; the data is invalid.")
		};

		/// <summary>把透明像素合成到白色不透明画布上。</summary>
		private static SKBitmap FlattenToWhite(SKBitmap source)
		{
			var info = new SKImageInfo(source.Width, source.Height, source.ColorType, SKAlphaType.Opaque);
			using var surface = SKSurface.Create(info);
			surface.Canvas.Clear(SKColors.White);
			surface.Canvas.DrawBitmap(source, 0, 0);
			surface.Flush();
			using var snapshot = surface.Snapshot();
			return SKBitmap.FromImage(snapshot);
		}

		/// <summary>
		/// 按 EXIF 方向展开成真实的像素方向（手机照片常见）。方向已是默认（TopLeft）时返回 null
		/// 表示「无须变换」，其余 7 种用对应矩阵重画一次；返回的位图归调用方释放。
		/// </summary>
		private static SKBitmap? ApplyEncodedOrientation(SKBitmap source, SKEncodedOrigin origin)
		{
			// 矩阵以「目标像素坐标 → 源像素坐标」表示（逆映射），逐 EXIF 值推导；
			// TopLeft 之外的 7 种方向见 Skia 的 SKEncodedOrigin 定义。
			var (matrix, width, height) = origin switch
			{
				SKEncodedOrigin.TopLeft => (SKMatrix.Identity, source.Width, source.Height),
				SKEncodedOrigin.TopRight => (
                    Affine(-1, 0, source.Width - 1, 0, 1, 0), source.Width, source.Height),
				SKEncodedOrigin.BottomRight => (
                    Affine(-1, 0, source.Width - 1, 0, -1, source.Height - 1), source.Width, source.Height),
				SKEncodedOrigin.BottomLeft => (
                    Affine(1, 0, 0, 0, -1, source.Height - 1), source.Width, source.Height),
				SKEncodedOrigin.LeftTop => (
                    Affine(0, 1, 0, 1, 0, 0), source.Height, source.Width),
				SKEncodedOrigin.RightTop => (
                    Affine(0, 1, 0, -1, 0, source.Height - 1), source.Height, source.Width),
				SKEncodedOrigin.RightBottom => (
                    Affine(0, -1, source.Height - 1, -1, 0, source.Width - 1), source.Height, source.Width),
				SKEncodedOrigin.LeftBottom => (
                    Affine(0, -1, source.Width - 1, 1, 0, 0), source.Height, source.Width),
				_ => throw new InvalidDataException(
					$"Unknown encoded origin '{origin}'; the image data is invalid.")
			};

			if (origin == SKEncodedOrigin.TopLeft)
				return null;

			using var surface = SKSurface.Create(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
			var canvas = surface.Canvas;
			canvas.SetMatrix(matrix);
			canvas.DrawBitmap(source, 0, 0);
			canvas.ResetMatrix();
			surface.Flush();
			using var snapshot = surface.Snapshot();
			return SKBitmap.FromImage(snapshot);
		}

		/// <summary>
		/// 构造一个仿射矩阵（透视分量保持单位值）。3.x 的 SKMatrix 不再提供九参工厂，
		/// 但九个分量是公有可写属性，直接初始化即可。
		/// </summary>
		private static SKMatrix Affine(
			float scaleX, float skewX, float transX, float skewY, float scaleY, float transY)
			=> new()
			{
				ScaleX = scaleX,
				SkewX = skewX,
				TransX = transX,
				SkewY = skewY,
				ScaleY = scaleY,
				TransY = transY,
				Persp2 = 1
			};
	}
}
