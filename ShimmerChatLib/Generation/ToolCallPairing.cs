using SharperLLM.API;

namespace ShimmerChatLib.Generation
{
    /// <summary>一条尚未被 tool_result 应答的工具调用。</summary>
    public readonly record struct UnansweredToolCall(Message Assistant, int CallIndex, ToolCall Call);

    /// <summary>
    /// 工具调用与工具结果的配对规则（唯一的实现，供历史完整性检查复用）。
    /// <para>
    /// 配对范围：一条 assistant 消息的 toolCalls，只与<strong>紧随其后的连续 tool_result 消息</strong>配对。
    /// 优先按 <c>tool_call.id</c> 匹配；部分 API 不回传稳定 id，此时按顺序回退。
    /// </para>
    /// </summary>
    public static class ToolCallPairing
    {
        /// <summary>枚举全部未应答的工具调用（按对话顺序）。</summary>
        public static IReadOnlyList<UnansweredToolCall> EnumerateUnanswered(IReadOnlyList<Message> messages)
        {
            var result = new List<UnansweredToolCall>();

            for (int i = 0; i < messages.Count; i++)
            {
                var assistant = messages[i];
                if (assistant.sender != Sender.AI)
                    continue;

                var calls = assistant.CurrentVersion?.toolCalls;
                if (calls is not { Count: > 0 })
                    continue;

                // 紧随其后的连续 tool_result 段
                var results = new List<Message>();
                int j = i + 1;
                while (j < messages.Count && messages[j].sender == Sender.ToolResult)
                {
                    results.Add(messages[j]);
                    j++;
                }

                var answeredIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in results)
                {
                    var id = r.CurrentVersion?.id;
                    if (!string.IsNullOrEmpty(id))
                        answeredIds.Add(id);
                }

                int positionalCursor = 0;
                for (int c = 0; c < calls.Count; c++)
                {
                    var call = calls[c];
                    bool answered;
                    if (!string.IsNullOrEmpty(call.id))
                    {
                        answered = answeredIds.Contains(call.id);
                    }
                    else
                    {
                        answered = positionalCursor < results.Count;
                        positionalCursor++;
                    }

                    if (!answered)
                        result.Add(new UnansweredToolCall(assistant, c, call));
                }
            }

            return result;
        }

        /// <summary>是否存在未应答的工具调用。</summary>
        public static bool AnyUnanswered(IReadOnlyList<Message> messages)
            => EnumerateUnanswered(messages).Count > 0;
    }
}
