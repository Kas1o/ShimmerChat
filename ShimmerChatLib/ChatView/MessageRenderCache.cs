using Microsoft.AspNetCore.Components;
using SharperLLM.Util;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.ChatView;

/// <summary>
/// 单条消息的一段正文的渲染结果缓存（正文与思考各用一个实例）。
/// <para>
/// 渲染管线（Markdig、公式、代码高亮、SVG）是消息气泡里最重的工作。气泡会在流式增量、
/// 宿主刷新、界面重绘时被反复重渲染；如果每次都重跑管线，一次重渲染就会退化为
/// 「消息数 × 完整 Markdown 解析」，滚动或生成长回答时开销成倍放大。
/// </para>
/// <para>
/// 缓存键必须覆盖所有会影响渲染结果的因素：消息版本（切换版本会换实例）、原始文本、
/// 宿主上下文（切换对话 / 重新加载 Agent 会换实例）、当前语言，以及 Agent / Chat 上盖印的渲染树，
/// 避免沿用旧管线或旧语言的输出。内容没变时直接复用上次的 HTML。
/// </para>
/// <para>
/// 内置对话界面与插件对话界面都可以直接复用；宿主侧的
/// <c>ChatMessageBubble</c> 即用它缓存正文与思考。
/// </para>
/// </summary>
public sealed class MessageRenderCache
{
	private readonly IMessageDisplayService _display;

	private bool _hasValue;
	private ChatMessage? _version;
	private ChatViewContext? _context;
	private string? _culture;
	private string? _text;
	private string? _agentRenderTree;
	private string? _chatRenderTree;
	private MarkupString _value;

	public MessageRenderCache(IMessageDisplayService display)
	{
		_display = display;
	}

	/// <summary>返回 <paramref name="text"/> 在当前上下文下的渲染结果；命中缓存时不重新执行管线。</summary>
	public MarkupString Get(string? text, ChatMessage? version, ChatViewContext context, string? culture = null)
	{
		var agentRenderTree = context.Agent.RenderModifierTreeJson;
		var chatRenderTree = context.Chat.SourceRenderModifierTreeJson;

		if (_hasValue
			&& ReferenceEquals(_version, version)
			&& ReferenceEquals(_context, context)
			&& string.Equals(_culture, culture, StringComparison.Ordinal)
			&& string.Equals(_text, text, StringComparison.Ordinal)
			&& ReferenceEquals(_agentRenderTree, agentRenderTree)
			&& ReferenceEquals(_chatRenderTree, chatRenderTree))
		{
			return _value;
		}

		// 空内容也走管线：渲染修改树可能对空文本有副作用（例如注入占位），
		// 由服务而不是缓存决定空文本的输出。
		_value = _display.Render(text ?? string.Empty, context.Chat, context.Agent);
		_hasValue = true;
		_version = version;
		_context = context;
		_culture = culture;
		_text = text;
		_agentRenderTree = agentRenderTree;
		_chatRenderTree = chatRenderTree;
		return _value;
	}
}
