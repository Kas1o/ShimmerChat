using SharperLLM.Util;

namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 用户上传图像的存储方式。
    /// </summary>
    public enum ImageStorageMode
    {
        /// <summary>
        /// 写入 UserUploadImage 目录，消息里只保存文件名引用（默认）。
        /// 消息体积小、消息存储不被 Base64 撑大。
        /// </summary>
        UserUploadImage,

        /// <summary>
        /// Base64 直接写入消息，随消息一起持久化（可随对话导出/迁移，代价是消息体积大）。
        /// </summary>
        MessageBase64
    }

    /// <summary>
    /// 用户上传图像的存储与解析服务。
    /// <para>
    /// 它负责两件事：把上传的图像按当前设置持久化（落盘 / 内联），
    /// 以及在构建 LLM 请求前把消息里的图像附件解析成 <see cref="ChatImage"/>
    /// （SharperLLM 侧只处理 Base64，不接触文件系统）。
    /// </para>
    /// </summary>
    public interface IImageAttachmentService
    {
        /// <summary>当前生效的存储方式（每次读取设置，切换后立即生效）。</summary>
        ImageStorageMode Mode { get; }

        /// <summary>
        /// 当前生效的上传处理参数；null 一律表示「字节原样存储，不做处理」（原图模式、
        /// 配置缺失或非法时的回退值都是 null）。默认实现同样返回 null，
        /// 兼容没有处理能力的旧实现。每次读取，设置切换后对新的上传立即生效。
        /// </summary>
        ImageProcessingOptions? UploadProcessing => null;

        /// <summary>
        /// 保存一张用户上传的图像，返回可写入消息的图像附件。
        /// 图像格式不受支持时抛出 <see cref="NotSupportedException"/>。
        /// </summary>
        Task<MessageImage> SaveAsync(Stream content, string contentType);

        /// <summary>
        /// 保存一张由工具生成的图像，并始终返回「文件引用」形态的附件
        /// （即使当前上传存储方式配置为 Base64）：生成结果需要可被其它消息 / 界面
        /// 通过稳定的 <c>/userimages/...</c> 链接再次访问。
        /// <para>
        /// 默认实现退化为 <see cref="SaveAsync"/>，用于兼容没有该能力的旧实现；
        /// 调用方必须检查返回的 <see cref="MessageImage.FileName"/> 并使用
        /// <see cref="GetDisplayUrl"/> 取链接，不能假定一定是文件。
        /// </para>
        /// </summary>
        Task<MessageImage> SaveGeneratedAsync(
            Stream content, string contentType, CancellationToken cancellationToken = default)
            => SaveAsync(content, contentType);

        /// <summary>
        /// 把消息中的图像附件解析为请求可用的内容块。
        /// 附件引用的文件缺失时抛出 <see cref="FileNotFoundException"/>，不静默跳过。
        /// </summary>
        Task<ChatImage> ToChatImageAsync(MessageImage image, CancellationToken cancellationToken = default);

        /// <summary>
        /// 解析图像附件为请求内容块，并在超出像素预算时先缩放 / 重编码。
        /// <paramref name="processing"/> 为 null 时等价于
        /// <see cref="ToChatImageAsync(MessageImage, CancellationToken)"/>（按存储原样发送）。
        /// <para>
        /// 处理只影响本次请求的内存副本，消息持久化的图像数据不被修改；
        /// 图像数据无法解码时显式抛 <see cref="InvalidDataException"/>，不静默跳图。
        /// </para>
        /// <para>
        /// 默认实现退化为原样解析，用于兼容没有处理能力的旧实现（如测试替身、虚拟会话）；
        /// 预算处理不生效时由实现记录日志，调用方不应假定预算一定被满足。
        /// </para>
        /// </summary>
        Task<ChatImage> ToChatImageAsync(
            MessageImage image, ImageProcessingOptions? processing, CancellationToken cancellationToken = default)
            => ToChatImageAsync(image, cancellationToken);

        /// <summary>
        /// 取得界面显示该图像用的 URL（内联 Base64 为 data URL，文件存储为 /userimages/...）。
        /// 图像数据缺失（例如引用的文件已被删除）时返回 null，由调用方明确提示而不是显示空白。
        /// </summary>
        string? GetDisplayUrl(MessageImage image);

        /// <summary>
        /// 删除文件存储的图像文件；用于「已上传但尚未发送就被丢弃」的图像，
        /// 避免 UserUploadImage 目录里积累孤儿文件。
        /// </summary>
        void DeleteStoredFile(MessageImage image);
    }
}
