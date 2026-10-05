namespace ShimmerChatLib.ChatView
{
	/// <summary>
	/// 对话界面契约。实现该接口的 Blazor 组件 + <see cref="ChatViewAttribute"/>
	/// 即被 <see cref="IChatViewRegistry"/> 发现，可被 Agent 选中。
	/// <para>
	/// 宿主只通过 <c>DynamicComponent</c> 传入 <see cref="Context"/> 一个参数，
	/// 其余数据（消息渲染服务、KV 存储、本地化、JS）由组件自行注入。
	/// </para>
	/// <example>
	/// <code>
	/// @implements IChatView
	/// [ChatView("plugin.chatview.mine", "chatview.mine")]
	/// public partial class MyChatView : ComponentBase
	/// {
	///     [Parameter] public ChatViewContext Context { get; set; } = default!;
	/// }
	/// </code>
	/// </example>
	/// </summary>
	public interface IChatView
	{
		/// <summary>
		/// 宿主上下文：活对象（Chat / Agent）、生成状态与宿主能力方法。
		/// 实现必须把它声明为 <c>[Parameter]</c> 属性，否则 DynamicComponent 渲染时缺少参数会抛异常。
		/// </summary>
		ChatViewContext Context { get; set; }
	}
}
