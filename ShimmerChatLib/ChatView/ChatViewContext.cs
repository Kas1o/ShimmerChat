using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShimmerChatLib.ChatView
{
	/// <summary>
	/// 对话界面的宿主契约。
	/// <para>
	/// 界面拿到的是<strong>活对象与能力方法</strong>，而不是页面实例：
	/// 宿主只向内暴露这一份契约，界面不持有页面引用，页面的生命周期、内部状态
	/// 与实现细节都不会泄漏出去；同时 <see cref="Chat"/> / <see cref="Agent"/>
	/// 就是宿主正在使用的同一实例（不是快照），因此不会出现界面数据与宿主不同步的问题。
	/// </para>
	/// <para>
	/// 宿主在本上下文失效时（切换对话、重新加载 Agent、切换界面）重建新实例，
	/// 界面不应长期缓存旧上下文。
	/// </para>
	/// </summary>
	public sealed class ChatViewContext
	{
		/// <summary>当前对话（与宿主同一实例）。</summary>
		public required Chat Chat { get; init; }

		/// <summary>当前 Agent（与宿主同一实例）。</summary>
		public required Agent Agent { get; init; }

		/// <summary>当前对话的消息列表（即 <see cref="Chat"/>.Messages）。</summary>
		public IReadOnlyList<Message> Messages => Chat.Messages;

		/// <summary>当前是否存在活跃生成（每次读取都取宿主实时状态）。</summary>
		public required Func<bool> IsGenerating { get; init; }

		/// <summary>当前生成阶段 pre / gen / post；null 表示未在生成。</summary>
		public required Func<string?> GenerationPhase { get; init; }

		/// <summary>发送一条纯文本用户消息并启动生成（等价于用户在输入框回车）。</summary>
		public required Func<string, Task> SendAsync { get; init; }

		/// <summary>
		/// 发送一条带图像的用户消息并启动生成。图像为空时等价于 <see cref="SendAsync"/>；
		/// 文本为空但存在图像时同样发送（仅携带图像）。
		/// 传入的是界面已持久化的图像附件，如何解析成请求内容由宿主负责。
		/// </summary>
		public required Func<string, IReadOnlyList<MessageImage>, Task> SendWithImagesAsync { get; init; }

		/// <summary>停止当前生成。</summary>
		public required Func<Task> StopGenerationAsync { get; init; }

		/// <summary>删除单条消息。</summary>
		public required Func<Message, Task> DeleteMessageAsync { get; init; }

		/// <summary>删除该消息及其之后的所有消息（Shift + 删除）。</summary>
		public required Func<Message, Task> DeleteMessagesFromAsync { get; init; }

		/// <summary>从该消息重新生成。</summary>
		public required Func<Message, Task> RegenerateFromAsync { get; init; }

		/// <summary>从该消息继续生成（续写）。</summary>
		public required Func<Message, Task> ContinueFromAsync { get; init; }

		/// <summary>标记对话已修改并持久化。</summary>
		public required Action MarkDirty { get; init; }

		/// <summary>请求宿主重绘界面（界面在非渲染线程改动数据后调用）。</summary>
		public required Func<Task> RequestRefreshAsync { get; init; }

		/// <summary>返回 Agent 页面。导航由宿主负责，界面不直接操作路由。</summary>
		public required Func<Task> NavigateBackAsync { get; init; }

		/// <summary>
		/// 宿主请求滚动到底部（生成过程中持续触发）。
		/// 是否真的滚动由界面决定（例如用户手动上滚时应当忽略）。
		/// </summary>
		public event Action? ScrollToBottomRequested;

		/// <summary>
		/// 宿主请求把文本写入界面输入框（插件面板调用
		/// <c>ChatPanelContext.InsertIntoInputAsync</c>）。
		/// 界面在挂载时设置、卸载时清理；返回是否已处理。
		/// </summary>
		public Func<string, bool, Task<bool>>? InputInsertHandler { get; set; }

		/// <summary>由宿主调用：通知界面滚动到底部。</summary>
		public void NotifyScrollToBottom() => ScrollToBottomRequested?.Invoke();

		/// <summary>由宿主调用：请求界面把文本写入输入框（未挂载输入框时返回 false）。</summary>
		public Task<bool> RequestInputInsertAsync(string text, bool append)
			=> InputInsertHandler?.Invoke(text, append) ?? Task.FromResult(false);
	}
}
