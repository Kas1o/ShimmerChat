using System;
using System.Threading.Tasks;

namespace ShimmerChatLib.Panel
{
	/// <summary>
	/// 聊天插件面板的宿主上下文。
	/// <para>
	/// 面板拿到的是<strong>活对象和能力方法</strong>，而不是页面实例：
	/// 页面只向内暴露这一份契约，面板不持有页面引用，因此页面自身的生命周期、
	/// 内部状态与实现细节都不会泄漏到插件里；同时因为 <see cref="Chat"/> /
	/// <see cref="Agent"/> 是页面正在使用的同一实例（不是快照），
	/// 也不会出现「面板数据与页面不同步」的问题。
	/// </para>
	/// <para>
	/// 旧参数（ChatGuid / AgentGuid / EventHandlerReg）继续注入，既有面板无需改动。
	/// </para>
	/// </summary>
	public class ChatPanelContext
	{
		/// <summary>当前对话对象（与页面同一实例，可直接读写 Messages）。</summary>
		public required Chat Chat { get; init; }

		/// <summary>当前 Agent 对象（与页面同一实例）。</summary>
		public required Agent Agent { get; init; }

		/// <summary>消息持久化服务：面板自行增删消息时使用。</summary>
		public required Interface.IMessageStoreService MessageStore { get; init; }

		/// <summary>
		/// 面板共享的输入草稿。按 (Chat, Agent) 由宿主缓存，
		/// 面板折叠、重新渲染或切回时草稿都不会丢失。
		/// </summary>
		public string Draft
		{
			get => DraftStore.GetOrCreate(DraftKey, () => "");
			set => DraftStore[DraftKey] = value ?? "";
		}

		/// <summary>输入草稿的缓存键。</summary>
		public string DraftKey => $"{Chat.Guid:N}|{Agent.Guid:N}";

		/// <summary>宿主提供的草稿存储。</summary>
		public required Interface.IPanelDraftStore DraftStore { get; init; }

		/// <summary>页面当前是否存在活跃生成。</summary>
		public required Func<bool> IsGenerating { get; init; }

		/// <summary>请求宿主重绘界面（面板在后台线程改动 Chat 后调用）。</summary>
		public required Func<Task> RequestRefreshAsync { get; init; }

		/// <summary>
		/// 请求宿主直接发送一条用户消息并启动生成。生成中或页面不可用时返回 false。
		/// </summary>
		public required Func<string, Task<bool>> SendUserMessageAsync { get; init; }

		/// <summary>
		/// 请求宿主把文本写入聊天主输入框（不发送）。页面不可用时返回 false。
		/// </summary>
		public required Func<string, bool, Task<bool>> InsertIntoInputAsync { get; init; }

		/// <summary>
		/// 登记一个面板事件处理器，用于接收用户消息 / AI 消息 / 工具结果通知。
		/// 等价于 <c>EventHandlerReg</c>，供只拿到 <see cref="ChatPanelContext"/> 的面板使用。
		/// </summary>
		public required Action<IChatPanelEventHandler> RegisterEventHandler { get; init; }

		/// <summary>宿主页面名称，仅用于日志与调试。</summary>
		public string HostName { get; init; } = "AgentChatPage";

		/// <summary>
		/// 面板在本面板界面内直接发送消息。
		/// 消息会以当前对话的用户消息入队并启动生成，等价于用户在输入框回车。
		/// </summary>
		public Task<bool> SendAsync(string message)
		{
			if (string.IsNullOrWhiteSpace(message)) return Task.FromResult(false);
			return SendUserMessageAsync(message);
		}
	}
}
