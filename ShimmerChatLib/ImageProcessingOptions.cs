namespace ShimmerChatLib
{
	/// <summary>
	/// 用户上传图像进入存储前的处理模式。
	/// </summary>
	public enum ImageUploadProcessingMode
	{
		/// <summary>
		/// 原图模式：字节原样存储，不做任何缩放与压缩（默认，与旧版行为完全一致）。
		/// </summary>
		Original = 0,

		/// <summary>
		/// 处理模式：像素总量超过预算的图像先等比缩放到预算内，
		/// 再按所选压缩算法重编码后存储。不超预算的图像保持原样，零质量损失。
		/// </summary>
		Processed = 1
	}

	/// <summary>
	/// 图像超出像素预算、被处理时使用的重编码算法。
	/// </summary>
	public enum ImageCompressionAlgorithm
	{
		/// <summary>仅按像素预算缩放，保持原格式重编码（PNG 仍是无损，体积收益有限）。</summary>
		None = 0,

		/// <summary>重编码为有损 WebP：体积收益最大，质量参数生效，保留透明通道。</summary>
		WebP = 1,

		/// <summary>重编码为有损 JPEG：兼容性最好，质量参数生效，透明通道合成白底。</summary>
		Jpeg = 2
	}

	/// <summary>
	/// 加载对话消息节点（<c>AppendChatMessagesNode</c>）对发给模型的图像采用的处理策略。
	/// 历史图像每轮都会重发，超预算的大图会持续放大请求体积、直到被端点拒绝；
	/// 该策略在解析为请求内容块时把超预算图像压回预算内。
	/// </summary>
	public enum ChatImageRequestProcessing
	{
		/// <summary>
		/// 按存储原样发送：消息里存的是什么就发什么，不做处理（默认）。
		/// 旧节点树反序列化后落到该值，行为不变。
		/// </summary>
		AsStored = 0,

		/// <summary>
		/// 预算处理：解析出的图像超过像素预算时，先缩放 / 压缩再进入请求。
		/// 处理只发生在请求构建阶段，消息里持久化的数据不被修改。
		/// </summary>
		Budgeted = 1
	}

	/// <summary>
	/// 一次图像处理（缩放 + 重编码）的完整参数。
	/// 全局「上传处理模式」与「加载对话消息节点」的预算处理共用同一形状，
	/// 保证两处的语义与取值范围只有一处定义。
	/// </summary>
	public sealed record ImageProcessingOptions
	{
		/// <summary>像素预算：<c>width × height</c> 的上限，超过即等比缩放到预算内。</summary>
		public long PixelBudget { get; init; } = ImageAttachmentRules.DefaultPixelBudget;

		/// <summary>缩放后重编码使用的算法。</summary>
		public ImageCompressionAlgorithm Algorithm { get; init; } = ImageCompressionAlgorithm.WebP;

		/// <summary>有损编码的质量（1-100）。</summary>
		public int Quality { get; init; } = ImageAttachmentRules.DefaultCompressionQuality;
	}

	/// <summary>
	/// 工具 / 端点产物图像的输出处理策略。
	/// 文生图 Profile、MCP 端点等配置级别持久化该策略，落盘前据此折算实际参数。
	/// </summary>
	public enum ToolImageOutputProcessing
	{
		/// <summary>跟随全局「上传图像处理方式」设置（默认，旧配置缺省落到此值）。</summary>
		FollowGlobal = 0,

		/// <summary>原样存储，不处理：画质优先，接受磁盘与请求体积代价。</summary>
		Original = 1,

		/// <summary>按自定义参数处理：超像素预算先缩放，再按指定算法重编码。</summary>
		Custom = 2
	}

	/// <summary>
	/// 配置级别（文生图 Profile、MCP 端点）持久化的图像输出处理配置。
	/// 与全局上传处理设置折算出实际生效的 <see cref="ImageProcessingOptions"/>。
	/// </summary>
	public sealed class ImageProcessingConfig
	{
		public ToolImageOutputProcessing Mode { get; set; } = ToolImageOutputProcessing.FollowGlobal;

		/// <summary>Custom 模式的像素预算；越界值在折算时收敛，配置校验负责提前提示。</summary>
		public long PixelBudget { get; set; } = ImageAttachmentRules.DefaultPixelBudget;

		/// <summary>Custom 模式的压缩算法。</summary>
		public ImageCompressionAlgorithm Algorithm { get; set; } = ImageCompressionAlgorithm.WebP;

		/// <summary>Custom 模式的有损编码质量。</summary>
		public int Quality { get; set; } = ImageAttachmentRules.DefaultCompressionQuality;

		/// <summary>
		/// 折算实际生效的处理参数：<b>null 表示原样存储，不处理</b>。
		/// <paramref name="global"/> 是当前全局上传处理设置（原图模式下为 null）。
		/// </summary>
		public ImageProcessingOptions? Resolve(ImageProcessingOptions? global) => Mode switch
		{
			ToolImageOutputProcessing.Original => null,
			ToolImageOutputProcessing.Custom => new ImageProcessingOptions
			{
				PixelBudget = ImageAttachmentRules.ClampPixelBudget(PixelBudget),
				Algorithm = Algorithm,
				Quality = ImageAttachmentRules.ClampCompressionQuality(Quality)
			},
			_ => global
		};
	}

	/// <summary>
	/// 输出处理配置的解析入口。配置缺失（旧数据无该字段被序列化成 null）时
	/// 与「跟随全局」等价，避免每处调用都要写 null 检查。
	/// 注意不能写成 <c>config?.Resolve(global) ?? global</c>：
	/// Original 模式的 Resolve <b>有意</b>返回 null（不处理），`??` 会把它误折成全局值。
	/// </summary>
	public static class ImageProcessingConfigExtensions
	{
		public static ImageProcessingOptions? ResolveOutput(
			this ImageProcessingConfig? config, ImageProcessingOptions? global)
			=> config != null ? config.Resolve(global) : global;
	}
}
