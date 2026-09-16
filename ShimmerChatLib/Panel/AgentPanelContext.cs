using System;
using System.Collections.Generic;

namespace ShimmerChatLib.Panel
{
	/// <summary>
	/// Agent 级插件面板（<see cref="PanelDisplayPlace.Agent"/>）的宿主上下文。
	/// <para>
	/// 与 <see cref="ChatPanelContext"/> 对应：面板拿到 Agent 级<strong>活对象与基础设施</strong>，
	/// 而不是页面实例；因为传入的是页面正在使用的同一 <see cref="ShimmerChatLib.Agent"/> 实例
	/// （不是快照），面板读写与页面天然一致。
	/// </para>
	/// <para>
	/// Agent 编辑页没有「当前对话」，因此 <see cref="Chat"/> 为 null；
	/// 需要对话级数据的面板应在 Agent 页之外改用 <see cref="ChatPanelContext"/>。
	/// </para>
	/// <para>
	/// 旧参数（AgentGuid / EventHandlerReg）继续注入，既有面板无需改动。
	/// </para>
	/// </summary>
	public class AgentPanelContext
	{
		/// <summary>当前 Agent 对象（与页面同一实例）。</summary>
		public required Agent Agent { get; init; }

		/// <summary>
		/// 当前对话对象。Agent 编辑页没有选中的对话，因此为 null。
		/// </summary>
		public Chat? Chat { get; init; }

		/// <summary>消息持久化服务。</summary>
		public required Interface.IMessageStoreService MessageStore { get; init; }

		/// <summary>宿主提供的草稿存储（与聊天面板共用，键由面板自行约定）。</summary>
		public required Interface.IPanelDraftStore DraftStore { get; init; }

		/// <summary>面板草稿：按 Agent 缓存，折叠、重新渲染或切回时都不会丢失。</summary>
		public string Draft
		{
			get => DraftStore.GetOrCreate(DraftKey, () => "");
			set => DraftStore[DraftKey] = value ?? "";
		}

		/// <summary>输入草稿的缓存键。</summary>
		public string DraftKey => $"agent:{Agent.Guid:N}";

		/// <summary>请求宿主重绘界面（面板在后台线程改动 Agent 后调用）。</summary>
		public required Func<System.Threading.Tasks.Task> RequestRefreshAsync { get; init; }

		/// <summary>
		/// 登记一个面板事件处理器，等价于 <c>EventHandlerReg</c>，
		/// 供只拿到 <see cref="AgentPanelContext"/> 的面板使用。
		/// </summary>
		public required Action<IChatPanelEventHandler> RegisterEventHandler { get; init; }

		/// <summary>宿主页面名称，仅用于日志与调试。</summary>
		public string HostName { get; init; } = "AgentPage";
	}
}
