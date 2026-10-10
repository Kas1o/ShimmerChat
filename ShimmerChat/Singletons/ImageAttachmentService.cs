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
	/// 目录方式下消息里只有文件名，读取成 Base64 发生在这里（构建请求之前），
	/// 因此 SharperLLM 侧只需要处理 Base64 内容块。
	/// </para>
	/// </summary>
	public class ImageAttachmentService : IImageAttachmentService
	{
		/// <summary>存储方式设置的 KV 空间与键。</summary>
		public const string SettingsSpace = "App";
		public const string StorageModeKey = "image_storage_mode";

		/// <summary>图像文件目录（同时由 Program.cs 以 /userimages 暴露为静态资源）。</summary>
		public static readonly string UploadDirectory = Path.Combine(AppContext.BaseDirectory, "UserUploadImage");

		/// <summary>支持的图像格式与落盘扩展名。</summary>
		private static readonly Dictionary<string, string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
		{
			["image/png"] = ".png",
			["image/jpeg"] = ".jpg",
			["image/webp"] = ".webp",
			["image/gif"] = ".gif"
		};

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

		public async Task<MessageImage> SaveAsync(Stream content, string contentType)
		{
			var mimeType = ResolveMimeType(contentType);

			if (Mode == ImageStorageMode.MessageBase64)
			{
				using var buffer = new MemoryStream();
				await content.CopyToAsync(buffer);
				if (buffer.Length == 0)
					throw new InvalidDataException($"Uploaded image is empty ({contentType}).");
				return MessageImage.FromBase64(Convert.ToBase64String(buffer.ToArray()), mimeType);
			}

			Directory.CreateDirectory(UploadDirectory);
			var fileName = $"{Guid.NewGuid():N}{SupportedTypes[mimeType]}";
			var filePath = Path.Combine(UploadDirectory, fileName);
			await using (var file = File.Create(filePath))
			{
				await content.CopyToAsync(file);
			}

			if (new FileInfo(filePath).Length == 0)
			{
				// 空文件不是有效图像：删除并显式报错，不留下无法使用的引用
				File.Delete(filePath);
				throw new InvalidDataException($"Uploaded image is empty ({contentType}).");
			}

			return MessageImage.FromFile(fileName, mimeType);
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
