namespace ShimmerChatLib
{
	/// <summary>
	/// 消息持久化的图像附件。
	/// <para>
	/// 两种存储方式二选一：<see cref="Base64"/> 把图像数据内联在消息里，
	/// 或 <see cref="FileName"/> 指向 UserUploadImage 目录下的文件
	/// （由 <c>IImageAttachmentService</c> 在构建请求前读取为 Base64）。
	/// </para>
	/// </summary>
	public class MessageImage
	{
		/// <summary>MIME 类型，例如 image/png。</summary>
		public required string MimeType { get; set; }

		/// <summary>内联 Base64 数据（不含 data URL 前缀）。</summary>
		public string? Base64 { get; set; }

		/// <summary>UserUploadImage 目录下的文件名（只保存文件名，不含路径）。</summary>
		public string? FileName { get; set; }

		public static MessageImage FromBase64(string base64, string mimeType)
			=> new() { Base64 = base64, MimeType = mimeType };

		public static MessageImage FromFile(string fileName, string mimeType)
			=> new() { FileName = fileName, MimeType = mimeType };
	}
}
