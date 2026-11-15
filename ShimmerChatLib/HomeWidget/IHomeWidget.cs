namespace ShimmerChatLib.HomeWidget
{
	/// <summary>
	/// 首页 Widget 组件契约。宿主通过 <c>DynamicComponent</c> 渲染 Widget，
	/// 并且只传入一个 <see cref="Context"/> 参数。
	/// </summary>
	public interface IHomeWidget
	{
		/// <summary>
		/// 宿主提供的 Widget 上下文（实例配置与保存回调）。
		/// 实现必须把它声明为 <c>[Parameter]</c> 属性，否则 DynamicComponent 渲染时缺少参数会抛异常。
		/// </summary>
		HomeWidgetContext Context { get; set; }
	}
}
