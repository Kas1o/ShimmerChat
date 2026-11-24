using Microsoft.Extensions.Logging;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Singletons
{
	/// <summary>
	/// 用户上传图像存储与解析服务。
	/// <para>
	/// 存储方式由 KV 设置 <c>App/image_storage_mode</c> 决定（默认
	/// <see cref="ImageStorageMode.UserUploadImage"/>）：落盘到 UserUploadImage 目录并只保存文件名，
	/// 或直接以 Base64 内联进消息。设置每次读取，切换后立即对新的上传生效。
	/// </para>
	/// <para>
	/// 上传处理方式由 KV 设置 <c>App/image_upload_processing_mode</c> 决定（默认原图模式，
	/// 行为与旧版一致）：处理模式下，超过像素预算的图像先经 <see cref="ImageNormalizer"/>
	/// 缩放压缩，再按存储方式落盘 / 内联。
	/// </para>
	/// <para>
	/// 目录方式下消息里只有文件名，读取成 Base64 发生在这里（构建请求之前），
	/// 因此 SharperLLM 侧只需要处理 Base64 内容块。
	/// </para>
	/// </summary>
	public class ImageAttachmentService : IImageAttachmentService
	{
		/// <summary>存储方式设置的 KV 空间与键。</summary>
		public const string SettingsSpace = "App";
		public const string StorageModeKey = "image_storage_mode";

		/// <summary>上传处理模式及其参数的 KV 键（同一 KV 空间）。</summary>
		public const string UploadProcessingModeKey = "image_upload_processing_mode";
		public const string UploadPixelBudgetKey = "image_upload_pixel_budget";
		public const string UploadCompressionKey = "image_upload_compression";
		public const string UploadQualityKey = "image_upload_quality";
		public const string UploadAlwaysReencodeKey = "image_upload_always_reencode";

		/// <summary>图像文件目录（Data/UserUploadImage，同时由 Program.cs 以 /userimages 暴露为静态资源）。</summary>
		public static string UploadDirectory => ShimmerChatLib.Storage.DataPaths.Current.UserUploadImageDirectory;

		/// <summary>支持的图像格式与落盘扩展名（来源见 <see cref="ImageAttachmentRules"/>）。</summary>
		private static readonly Dictionary<string, string> SupportedTypes =
			ImageAttachmentRules.MimeToExtension.ToDictionary(
				pair => pair.Key,
				pair => pair.Value,
				StringComparer.OrdinalIgnoreCase);

		private readonly IKVDataService _kvData;
		private readonly ILogger<ImageAttachmentService> _logger;

		public ImageAttachmentService(IKVDataService kvData, ILogger<ImageAttachmentService> logger)
		{
			_kvData = kvData;
			_logger = logger;
		}

		public ImageStorageMode Mode
		{
			get
			{
				var raw = _kvData.Read(SettingsSpace, StorageModeKey);
				if (string.IsNullOrWhiteSpace(raw))
					return ImageStorageMode.UserUploadImage;

				if (Enum.TryParse<ImageStorageMode>(raw, ignoreCase: true, out var mode))
					return mode;

				// 设置属于边界数据：值非法时记录完整错误并回退到默认方式，
				// 否则界面会因为一个坏配置而完全无法渲染。
				_logger.LogError(
					"[ImageAttachmentService] Unknown image storage mode '{Raw}' in KV {Space}/{Key}; falling back to {Fallback}.",
					raw, SettingsSpace, StorageModeKey, ImageStorageMode.UserUploadImage);
				return ImageStorageMode.UserUploadImage;
			}
		}

		/// <summary>
		/// 当前生效的上传处理参数。原图模式（默认、配置非法或缺失）返回 null：
		/// null 一律意味着「字节原样存储，不做处理」，与旧版行为一致。
		/// </summary>
		public ImageProcessingOptions? UploadProcessing
		{
			get
			{
				var raw = _kvData.Read(SettingsSpace, UploadProcessingModeKey);
				if (string.IsNullOrWhiteSpace(raw))
					return null;

				if (!Enum.TryParse<ImageUploadProcessingMode>(raw, ignoreCase: true, out var mode)
					|| !Enum.IsDefined(mode))
				{
					_logger.LogError(
						"[ImageAttachmentService] Unknown upload processing mode '{Raw}' in KV {Space}/{Key}; " +
						"falling back to Original (store bytes as-is).",
						raw, SettingsSpace, UploadProcessingModeKey);
					return null;
				}

				if (mode == ImageUploadProcessingMode.Original)
					return null;

				return new ImageProcessingOptions
				{
					PixelBudget = ReadPixelBudget(),
					Algorithm = ReadCompressionAlgorithm(),
					Quality = ReadCompressionQuality(),
					AlwaysReencode = ReadAlwaysReencode()
				};
			}
		}

		/// <summary>读取「总是重编码」开关；非法或缺失时按默认关闭收敛并记录错误。</summary>
		private bool ReadAlwaysReencode()
		{
			var raw = _kvData.Read(SettingsSpace, UploadAlwaysReencodeKey);
			if (bool.TryParse(raw, out var enabled))
				return enabled;

			if (!string.IsNullOrWhiteSpace(raw))
			{
				// 空值视为「未配置」直接走默认关闭；非空但解析失败才是坏配置。
				_logger.LogError(
					"[ImageAttachmentService] Invalid always-reencode flag '{Raw}' in KV {Space}/{Key}; disabled.",
					raw, SettingsSpace, UploadAlwaysReencodeKey);
			}
			return false;
		}

		/// <summary>读取像素预算；非法或缺失时按默认收敛并记录错误。</summary>
		private long ReadPixelBudget()
		{
			var raw = _kvData.Read(SettingsSpace, UploadPixelBudgetKey);
			if (long.TryParse(raw, out var budget)
				&& budget >= ImageAttachmentRules.MinPixelBudget
				&& budget <= ImageAttachmentRules.MaxPixelBudget)
			{
				return budget;
			}

			_logger.LogError(
				"[ImageAttachmentService] Invalid pixel budget '{Raw}' in KV {Space}/{Key}; using {Default}.",
				raw, SettingsSpace, UploadPixelBudgetKey, ImageAttachmentRules.DefaultPixelBudget);
			return ImageAttachmentRules.DefaultPixelBudget;
		}

		/// <summary>读取压缩算法；非法或缺失时按默认收敛并记录错误。</summary>
		private ImageCompressionAlgorithm ReadCompressionAlgorithm()
		{
			var raw = _kvData.Read(SettingsSpace, UploadCompressionKey);
			if (Enum.TryParse<ImageCompressionAlgorithm>(raw, ignoreCase: true, out var algorithm)
				&& Enum.IsDefined(algorithm))
			{
				return algorithm;
			}

			_logger.LogError(
				"[ImageAttachmentService] Unknown compression algorithm '{Raw}' in KV {Space}/{Key}; using {Default}.",
				raw, SettingsSpace, UploadCompressionKey, ImageCompressionAlgorithm.WebP);
			return ImageCompressionAlgorithm.WebP;
		}

		/// <summary>读取压缩质量；非法或超界时按默认收敛并记录错误。</summary>
		private int ReadCompressionQuality()
		{
			var raw = _kvData.Read(SettingsSpace, UploadQualityKey);
			if (int.TryParse(raw, out var quality)
				&& quality >= ImageAttachmentRules.MinCompressionQuality
				&& quality <= ImageAttachmentRules.MaxCompressionQuality)
			{
				return quality;
			}

			_logger.LogError(
				"[ImageAttachmentService] Invalid compression quality '{Raw}' in KV {Space}/{Key}; using {Default}.",
				raw, SettingsSpace, UploadQualityKey, ImageAttachmentRules.DefaultCompressionQuality);
			return ImageAttachmentRules.DefaultCompressionQuality;
		}

		public async Task<MessageImage> SaveAsync(Stream content, string contentType)
		{
			var mimeType = ResolveMimeType(contentType);

			// 处理模式：图像必须先读入内存才能按像素预算处理；超预算的图像在此被缩放压缩。
			var processing = UploadProcessing;
			if (processing != null)
			{
				using var buffer = new MemoryStream();
				await content.CopyToAsync(buffer);
				var bytes = buffer.ToArray();
				if (bytes.Length == 0)
					throw new InvalidDataException($"Uploaded image is empty ({contentType}).");

				var result = ImageNormalizer.Process(bytes, mimeType, processing);
				return await StoreAsync(result.Data, result.MimeType);
			}

			if (Mode == ImageStorageMode.MessageBase64)
			{
				using var buffer = new MemoryStream();
				await content.CopyToAsync(buffer);
				if (buffer.Length == 0)
					throw new InvalidDataException($"Uploaded image is empty ({contentType}).");
				return MessageImage.FromBase64(Convert.ToBase64String(buffer.ToArray()), mimeType);
			}

			return await SaveFileAsync(content, mimeType, cancellationToken: default);
		}

		/// <summary>
		/// 生成结果始终落盘：工具返回的链接必须在消息之外也能被访问，
		/// 因此不受 <see cref="Mode"/> 的 Base64 设置影响。
		/// 是否按预算处理由工具 / 端点的输出处理配置决定（processing == null 即原图落盘），
		/// 调用方负责把「跟随全局 / 原图 / 自定义」折算成这里的参数。
		/// </summary>
		public async Task<MessageImage> SaveGeneratedAsync(
			Stream content, string contentType, CancellationToken cancellationToken = default)
			=> await SaveGeneratedAsync(content, contentType, processing: null, cancellationToken);

		/// <inheritdoc cref="IImageAttachmentService.SaveGeneratedAsync(Stream,string,ImageProcessingOptions?,CancellationToken)"/>
		public async Task<MessageImage> SaveGeneratedAsync(
			Stream content, string contentType, ImageProcessingOptions? processing,
			CancellationToken cancellationToken = default)
		{
			var mimeType = ResolveMimeType(contentType);

			// 原图落盘保持流式路径，不读入内存。
			if (processing == null)
				return await SaveFileAsync(content, mimeType, cancellationToken);

			// 处理必须先读入内存才能解码；20MiB 上限决定了这不会失控。
			using var buffer = new MemoryStream();
			await content.CopyToAsync(buffer, cancellationToken);
			var bytes = buffer.ToArray();
			if (bytes.Length == 0)
				throw new InvalidDataException($"Image is empty ({mimeType}).");

			var result = ImageNormalizer.Process(bytes, mimeType, processing);
			await using var processed = new MemoryStream(result.Data);
			return await SaveFileAsync(processed, result.MimeType, cancellationToken);
		}

		private static async Task<MessageImage> SaveFileAsync(
			Stream content, string mimeType, CancellationToken cancellationToken)
		{
			Directory.CreateDirectory(UploadDirectory);
			var fileName = $"{Guid.NewGuid():N}{SupportedTypes[mimeType]}";
			var filePath = Path.Combine(UploadDirectory, fileName);
			await using (var file = File.Create(filePath))
			{
				await content.CopyToAsync(file, cancellationToken);
			}

			if (new FileInfo(filePath).Length == 0)
			{
				// 空文件不是有效图像：删除并显式报错，不留下无法使用的引用
				File.Delete(filePath);
				throw new InvalidDataException($"Image is empty ({mimeType}).");
			}

			return MessageImage.FromFile(fileName, mimeType);
		}

		/// <summary>把已在内存中的图像字节按当前存储方式落盘 / 内联。</summary>
		private async Task<MessageImage> StoreAsync(byte[] data, string mimeType)
		{
			if (Mode == ImageStorageMode.MessageBase64)
				return MessageImage.FromBase64(Convert.ToBase64String(data), mimeType);

			await using var stream = new MemoryStream(data);
			return await SaveFileAsync(stream, mimeType, cancellationToken: default);
		}

		public async Task<ChatImage> ToChatImageAsync(MessageImage image, CancellationToken cancellationToken = default)
		{
			if (!string.IsNullOrEmpty(image.Base64))
				return ChatImage.FromBase64(image.Base64, image.MimeType);

			var filePath = ResolveFilePath(image);

			if (!File.Exists(filePath))
				throw new FileNotFoundException(
					$"Image file referenced by the message is missing: {filePath}. " +
					"Restore the file in UserUploadImage or remove the image from the message.", filePath);

			var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
			if (bytes.Length == 0)
				throw new InvalidDataException($"Image file is empty: {filePath}");

			return ChatImage.FromBase64(Convert.ToBase64String(bytes), image.MimeType);
		}

		/// <summary>
		/// 解析图像附件为请求内容块，超像素预算时先缩放 / 重编码。
		/// 处理只发生在内存副本上：消息里持久化的图像数据（文件或 Base64）不被修改。
		/// </summary>
		public async Task<ChatImage> ToChatImageAsync(
			MessageImage image, ImageProcessingOptions? processing, CancellationToken cancellationToken = default)
		{
			if (processing == null)
				return await ToChatImageAsync(image, cancellationToken);

			byte[] data;
			if (!string.IsNullOrEmpty(image.Base64))
			{
				data = Convert.FromBase64String(image.Base64);
			}
			else
			{
				var filePath = ResolveFilePath(image);

				if (!File.Exists(filePath))
					throw new FileNotFoundException(
						$"Image file referenced by the message is missing: {filePath}. " +
						"Restore the file in UserUploadImage or remove the image from the message.", filePath);

				data = await File.ReadAllBytesAsync(filePath, cancellationToken);
			}

			if (data.Length == 0)
				throw new InvalidDataException($"Image attachment is empty ({image.FileName ?? image.MimeType}).");

			var result = ImageNormalizer.Process(data, image.MimeType, processing);
			return ChatImage.FromBase64(Convert.ToBase64String(result.Data), result.MimeType);
		}

		public string? GetDisplayUrl(MessageImage image)
		{
			if (!string.IsNullOrEmpty(image.Base64))
				return $"data:{image.MimeType};base64,{image.Base64}";

			if (TryResolveFileName(image.FileName, out var fileName) && File.Exists(Path.Combine(UploadDirectory, fileName)))
				return $"/userimages/{fileName}";

			return null;
		}

		public void DeleteStoredFile(MessageImage image)
		{
			if (!TryResolveFileName(image.FileName, out var fileName))
				return;

			var filePath = Path.Combine(UploadDirectory, fileName);
			if (!File.Exists(filePath))
				return;

			try
			{
				File.Delete(filePath);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "[ImageAttachmentService] Failed to delete image file {FilePath}.", filePath);
			}
		}

		private static string ResolveFilePath(MessageImage image)
		{
			if (TryResolveFileName(image.FileName, out var fileName))
				return Path.Combine(UploadDirectory, fileName);

			throw new InvalidOperationException(
				"MessageImage carries neither Base64 data nor a file name; the message data is invalid.");
		}

		/// <summary>
		/// 文件名只允许是 UserUploadImage 下的普通文件名：带分隔符的值（被手工改坏或被恶意构造的
		/// 消息数据）一律拒绝，避免解析时读到目录之外的任意文件。
		/// </summary>
		private static bool TryResolveFileName(string? fileName, out string resolved)
		{
			resolved = fileName ?? string.Empty;
			if (string.IsNullOrWhiteSpace(fileName))
				return false;

			if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
				return false;

			return true;
		}

		private static string ResolveMimeType(string contentType)
		{
			// 浏览器可能带参数（image/png;charset=...），只取 MIME 本身
			var mimeType = contentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
			if (SupportedTypes.ContainsKey(mimeType))
				return mimeType;

			throw new NotSupportedException(
				$"Unsupported image type '{contentType}'. Supported types: {string.Join(", ", SupportedTypes.Keys)}.");
		}
	}
}
