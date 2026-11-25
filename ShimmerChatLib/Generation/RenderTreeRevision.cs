namespace ShimmerChatLib.Generation;

/// <summary>
/// 渲染树的修订号，用于让渲染节点的输出缓存随树 / 预设编辑失效。
/// <para>
/// <see cref="Root"/> 标识当前生效的根树（Agent / Chat 覆盖树或全局树），
/// <see cref="Presets"/> 标识整份渲染预设存储。两者都变化时才需要重新计算；
/// 只比较数值，不需要序列化或哈希整棵树。
/// </para>
/// </summary>
public readonly record struct RenderTreeRevision(long Root, long Presets)
{
	/// <summary>尚未关联任何树时的默认值。</summary>
	public static RenderTreeRevision Empty => new(0, 0);
}
