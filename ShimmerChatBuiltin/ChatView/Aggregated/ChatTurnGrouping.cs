using ShimmerChatLib;

namespace ShimmerChatBuiltin.ChatView.Aggregated;

/// <summary>工具结果消息，以及由工具调用 Id 反查出的工具名（查不到为 null）。</summary>
public sealed record ChatToolResult(Message Message, string? ToolName);

/// <summary>
/// 聚合回合中的一个步骤：一条 AI 消息，及其紧随其后的工具结果消息。
/// </summary>
public sealed record ChatTurnStep
{
	/// <summary>该步骤的 AI 消息；为 null 表示 AI 消息已不存在（例如被单独删除），只剩工具结果。</summary>
	public required Message? Assistant { get; init; }

	/// <summary>该步骤的 AI 消息触发的工具结果。</summary>
	public required IReadOnlyList<ChatToolResult> ToolResults { get; init; }

	/// <summary>
	/// 该步骤没有可见输出（正文为空，只有思考 / 工具调用），应默认折叠。
	/// 正在生成中的步骤、以及回合的最后一步永远不折叠，避免流式内容被藏起来。
	/// </summary>
	public required bool IsSilent { get; init; }

	/// <summary>该步骤的工具调用次数。</summary>
	public int ToolCount => Assistant?.CurrentVersion?.toolCalls?.Count ?? ToolResults.Count;
}

/// <summary>
/// 聚合回合内的显示块：要么是一个需要展开的步骤，要么是连续无输出步骤合并成的折叠活动块。
/// </summary>
public sealed record ChatTurnBlock
{
	/// <summary>true = 折叠的工具调用活动块（由连续的无输出步骤合并而成）。</summary>
	public required bool IsActivity { get; init; }

	/// <summary>该显示块包含的步骤（活动块可能含多个）。</summary>
	public required IReadOnlyList<ChatTurnStep> Steps { get; init; }

	/// <summary>该显示块的工具调用次数。</summary>
	public int ToolCount => Steps.Sum(s => s.ToolCount);
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
/// <item>回合内，正文为空、只有思考与工具调用的步骤默认合并折叠成一块「工具调用活动」。</item>
/// <item>回合的最后一步永不折叠，保证用户总能看到结果（含流式输出）。</item>
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
			// 只有延伸到消息列表末尾的回合才拥有「最后一步」的特权：
			// 中途被用户消息截断的回合，其尾部静默步骤仍然折叠。
			bool runTouchesTail = index >= messages.Count;

			units.Add(new ChatDisplayUnit
			{
				Messages = run,
				IsTurn = true,
				Blocks = BuildBlocks(run, runTouchesTail)
			});
		}

		return units;
	}

	/// <summary>AI 消息与工具结果消息参与聚合，其余（用户 / 系统）各自独立显示。</summary>
	private static bool IsAggregatable(Message message)
		=> message.sender == Sender.AI || message.sender == Sender.ToolResult;

	private static IReadOnlyList<ChatTurnBlock> BuildBlocks(IReadOnlyList<Message> run, bool runTouchesTail)
	{
		var steps = BuildSteps(run);

		if (runTouchesTail && steps.Count > 0 && steps[^1].IsSilent)
			steps[^1] = steps[^1] with { IsSilent = false };

		var blocks = new List<ChatTurnBlock>();
		var pendingActivity = new List<ChatTurnStep>();

		foreach (var step in steps)
		{
			if (step.IsSilent)
			{
				pendingActivity.Add(step);
				continue;
			}

			FlushActivity(blocks, pendingActivity);
			blocks.Add(new ChatTurnBlock { IsActivity = false, Steps = [step] });
		}

		FlushActivity(blocks, pendingActivity);
		return blocks;
	}

	private static void FlushActivity(List<ChatTurnBlock> blocks, List<ChatTurnStep> pending)
	{
		if (pending.Count == 0) return;
		blocks.Add(new ChatTurnBlock { IsActivity = true, Steps = pending.ToList() });
		pending.Clear();
	}

	private static List<ChatTurnStep> BuildSteps(IReadOnlyList<Message> run)
	{
		var toolNames = BuildToolNameMap(run);
		var steps = new List<ChatTurnStep>();
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

			// Id 缺失或对不上时（部分 API 不回传稳定 Id），按顺序回退到该 AI 消息的工具调用名
			if (assistant?.CurrentVersion?.toolCalls is { Count: > 0 } calls)
			{
				for (int i = 0; i < results.Count; i++)
				{
					if (results[i].ToolName == null && i < calls.Count)
						results[i] = results[i] with { ToolName = calls[i].name };
				}
			}

			steps.Add(new ChatTurnStep
			{
				Assistant = assistant,
				ToolResults = results,
				IsSilent = IsSilentStep(assistant, results)
			});
		}

		return steps;
	}

	/// <summary>正文为空的工具调用步骤视为「无输出」。生成中 / 后处理中的消息保持展开。</summary>
	private static bool IsSilentStep(Message? assistant, IReadOnlyList<ChatToolResult> results)
	{
		if (assistant == null)
			return results.Count > 0;

		if (assistant.IsGenerating)
			return false;

		if (!string.IsNullOrWhiteSpace(assistant.CurrentVersion?.Content))
			return false;

		return assistant.CurrentVersion?.toolCalls is { Count: > 0 };
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
