using ShimmerChatLib;

namespace ShimmerChatBuiltin.ChatView.Aggregated;

/// <summary>工具结果消息，以及由工具调用 Id 反查出的工具名（查不到为 null）。</summary>
public sealed record ChatToolResult(Message Message, string? ToolName);

/// <summary>
/// 一次工具调用交互：一条 AI 消息的<strong>工具调用部分</strong>，及其紧随其后的工具结果。
/// <see cref="Assistant"/> 为 null 表示对应的 AI 消息已不存在（例如被单独删除），只剩工具结果。
/// </summary>
public sealed record ChatToolInteraction
{
	public required Message? Assistant { get; init; }

	public required IReadOnlyList<ChatToolResult> ToolResults { get; init; }

	/// <summary>本次交互的工具调用次数。</summary>
	public int ToolCount => Assistant?.CurrentVersion?.toolCalls?.Count ?? ToolResults.Count;

	/// <summary>本次交互仍在生成中（用于让活动块默认展开，展示实时进度）。</summary>
	public bool IsGenerating => Assistant?.IsGenerating ?? false;
}

/// <summary>聚合回合内的显示块。</summary>
public abstract record ChatTurnBlock;

/// <summary>正文块：AI 消息的思考与正文（工具调用已在活动块中，不在此重复展示）。</summary>
public sealed record ChatTextBlock(Message Assistant) : ChatTurnBlock;

/// <summary>
/// 工具调用活动块：连续的「工具调用交互」。
/// <para>
/// 只要消息带有工具调用（哪怕周围没有其它工具调用）就归入此块，不再内联在正文气泡里，
/// 因此界面始终是「正文 + 一行可折叠的工具活动」。默认折叠；
/// 其中仍有交互在生成时默认展开，避免把实时进度藏起来。
/// </para>
/// </summary>
public sealed record ChatToolActivityBlock : ChatTurnBlock
{
	public required IReadOnlyList<ChatToolInteraction> Interactions { get; init; }

	/// <summary>该活动块的工具调用总次数。</summary>
	public int ToolCount => Interactions.Sum(i => i.ToolCount);

	/// <summary>是否默认展开。</summary>
	public bool IsOpen => Interactions.Any(i => i.IsGenerating);
}

/// <summary>
/// 聚合界面的显示单元：连续的 AI / 工具结果消息合并为一个回合，其余消息各自独立显示。
/// </summary>
public sealed record ChatDisplayUnit
{
	/// <summary>该单元覆盖的消息（顺序与原消息列表一致）。</summary>
	public required IReadOnlyList<Message> Messages { get; init; }

	/// <summary>true = 聚合回合（<see cref="Blocks"/> 非空）；false = 独立消息（用户 / 系统消息等）。</summary>
	public required bool IsTurn { get; init; }

	/// <summary>聚合回合的显示块；非聚合单元为空。</summary>
	public IReadOnlyList<ChatTurnBlock> Blocks { get; init; } = [];
}

/// <summary>
/// 聚合对话界面的分组规则（纯逻辑，便于测试）：
/// <list type="bullet">
/// <item>连续的 AI / 工具结果消息合并成一个回合（一个气泡）。</item>
/// <item>AI 消息的正文与它的工具调用<strong>分开显示</strong>：正文留在气泡里，
/// 工具调用（连同其工具结果）一律并入折叠的「工具调用活动」块，
/// 因此一条既有正文又调用工具的 AI 消息，其调用会与后续调用合并进下方同一个活动块。</item>
/// <item>连续的交互合并成一块活动；被正文隔开的交互各自成块。</item>
/// </list>
/// </summary>
public static class ChatTurnGrouping
{
	public static IReadOnlyList<ChatDisplayUnit> Build(IReadOnlyList<Message> messages)
	{
		var units = new List<ChatDisplayUnit>();
		int index = 0;

		while (index < messages.Count)
		{
			if (!IsAggregatable(messages[index]))
			{
				units.Add(new ChatDisplayUnit
				{
					Messages = [messages[index]],
					IsTurn = false
				});
				index++;
				continue;
			}

			int start = index;
			while (index < messages.Count && IsAggregatable(messages[index]))
				index++;

			var run = messages.Skip(start).Take(index - start).ToList();

			units.Add(new ChatDisplayUnit
			{
				Messages = run,
				IsTurn = true,
				Blocks = BuildBlocks(run)
			});
		}

		return units;
	}

	/// <summary>AI 消息与工具结果消息参与聚合，其余（用户 / 系统）各自独立显示。</summary>
	private static bool IsAggregatable(Message message)
		=> message.sender == Sender.AI || message.sender == Sender.ToolResult;

	private static IReadOnlyList<ChatTurnBlock> BuildBlocks(IReadOnlyList<Message> run)
	{
		var toolNames = BuildToolNameMap(run);
		var blocks = new List<ChatTurnBlock>();
		var pendingActivity = new List<ChatToolInteraction>();
		int index = 0;

		while (index < run.Count)
		{
			Message? assistant = null;
			if (run[index].sender == Sender.AI)
			{
				assistant = run[index];
				index++;
			}

			var results = new List<ChatToolResult>();
			while (index < run.Count && run[index].sender == Sender.ToolResult)
			{
				results.Add(new ChatToolResult(run[index], ResolveToolName(run[index], toolNames)));
				index++;
			}

			var toolCalls = assistant?.CurrentVersion?.toolCalls;

			// Id 缺失或对不上时（部分 API 不回传稳定 Id），按顺序回退到该 AI 消息的工具调用名
			if (toolCalls is { Count: > 0 })
			{
				for (int i = 0; i < results.Count; i++)
				{
					if (results[i].ToolName == null && i < toolCalls.Count)
						results[i] = results[i] with { ToolName = toolCalls[i].name };
				}
			}

			bool hasContent = !string.IsNullOrWhiteSpace(assistant?.CurrentVersion?.Content);
			bool isToolInteraction = toolCalls is { Count: > 0 } || results.Count > 0;

			if (hasContent)
			{
				// 正文自成一块；它的工具调用留给下方的活动块（先冲刷已有的活动）
				FlushActivity(blocks, pendingActivity);
				blocks.Add(new ChatTextBlock(assistant!));
			}
			else if (!isToolInteraction && assistant != null)
			{
				// 既无正文也无工具调用的 AI 消息（如被中止的生成）仍然显示，避免消息凭空消失
				FlushActivity(blocks, pendingActivity);
				blocks.Add(new ChatTextBlock(assistant));
			}

			if (isToolInteraction)
			{
				pendingActivity.Add(new ChatToolInteraction
				{
					Assistant = assistant,
					ToolResults = results
				});
			}
		}

		FlushActivity(blocks, pendingActivity);
		return blocks;
	}

	private static void FlushActivity(List<ChatTurnBlock> blocks, List<ChatToolInteraction> pending)
	{
		if (pending.Count == 0) return;
		blocks.Add(new ChatToolActivityBlock { Interactions = pending.ToList() });
		pending.Clear();
	}

	private static Dictionary<string, string> BuildToolNameMap(IReadOnlyList<Message> run)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var message in run)
		{
			var calls = message.CurrentVersion?.toolCalls;
			if (calls == null) continue;

			foreach (var call in calls)
			{
				if (string.IsNullOrEmpty(call.id)) continue;
				map.TryAdd(call.id, call.name);
			}
		}

		return map;
	}

	private static string? ResolveToolName(Message toolResult, Dictionary<string, string> toolNames)
	{
		var id = toolResult.CurrentVersion?.id;
		if (string.IsNullOrEmpty(id)) return null;
		return toolNames.TryGetValue(id, out var name) ? name : null;
	}
}
