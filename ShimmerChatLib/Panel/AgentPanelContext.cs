using System;
using System.Threading.Tasks;

namespace ShimmerChatLib.Panel
{
	/// <summary>
	/// Agent 作用域面板的宿主契约基类。
	/// </summary>
	/// <remarks>
	/// 「Agent 作用域」有两个语义不同的宿主：<strong>Agent 编辑器</strong>（编辑一个真实存在的
	/// Agent）与<strong>其它只持有配置记录的宿主</strong>。两者的公共部分放在本基类，
	/// 差异部分由派生类型在<strong>类型层面</strong>表达，而不是用 null 字段当哨兵值。
	/// 依赖具体配置类型的派生上下文由持有该类型的插件自行定义（例如内置插件中的
	/// <c>SubAgentPanelContext</c>）。
	/// </remarks>
	public abstract class AgentPanelContext
	{
		/// <summary>当前编辑目标的 Guid（Agent 或子代理配置）。</summary>
		public required Guid TargetGuid { get; init; }

		/// <summary>消息持久化服务。</summary>
		public required Interface.IMessageStoreService MessageStore { get; init; }

		/// <summary>宿主提供的草稿存储（与聊天面板共用）。</summary>
		public required Interface.IPanelDraftStore DraftStore { get; init; }

		/// <summary>面板草稿：按编辑目标缓存，折叠、重新渲染或切回时都不会丢失。</summary>
		public string Draft
		{
			get => DraftStore.GetOrCreate(DraftKey, () => "");
			set => DraftStore[DraftKey] = value ?? "";
		}

		/// <summary>草稿的缓存键，由派生类型按自身语义给出。</summary>
		public abstract string DraftKey { get; }

		/// <summary>请求宿主重绘界面（面板在后台线程改动数据后调用）。</summary>
		public required Func<Task> RequestRefreshAsync { get; init; }

		/// <summary>
		/// 登记一个面板事件处理器，等价于 <c>EventHandlerReg</c>，
		/// 供只拿到 <see cref="AgentPanelContext"/> 的面板使用。
		/// </summary>
		public required Action<IChatPanelEventHandler> RegisterEventHandler { get; init; }

		/// <summary>宿主页面名称，仅用于日志与调试。</summary>
		public string HostName { get; init; } = "AgentPage";
	}

	/// <summary>
	/// Agent 编辑器的宿主契约：宿主持有一个<strong>真实存在</strong>的 <see cref="ShimmerChatLib.Agent"/>，
	/// 且该页面是 Agent 配置页而非对话页，因此不提供对话对象
	/// （需要对话数据的面板应使用 <see cref="ChatPanelContext"/> 并只在聊天侧栏出现）。
	/// </summary>
	public class LiveAgentPanelContext : AgentPanelContext
	{
		/// <summary>当前 Agent 活对象（与页面同一实例，不是快照）。</summary>
		public required Agent Agent { get; init; }

		/// <summary>该 Agent 参与的全部对话 Guid（实时读取 Agent.ChatGuids）。</summary>
		public Guid[] ChatGuids => [.. Agent.ChatGuids];

		public override string DraftKey => $"agent:{Agent.Guid:N}";

		/// <summary>宿主页面名称。</summary>
		public LiveAgentPanelContext() => HostName = "AgentPage";
	}
}
