using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Generation.ToolUi;

namespace ShimmerChatBuiltin.AskUser
{
    /// <summary>
    /// 向用户提问并等待回答。数据结构化接口 <see cref="IToolV3"/>，实现"现时交互"语义：
    /// <para>
    /// 请求发出后工具在内部等待（默认无限），用户提交 → 答案作为工具结果返回，本轮 loop 继续；
    /// 用户跳过 → 返回 skipped；生成被取消（用户点停止 / 发新消息）→ 抛
    /// <see cref="OperationCanceledException"/>，由执行节点中止本轮，不写伪造结果。
    /// </para>
    /// <para>
    /// 没有可展示交互的上下文（例如子代理的虚拟会话）会立刻返回
    /// <see cref="ToolErrorCodes.InteractionUnavailable"/>，绝不无声空等。
    /// </para>
    /// </summary>
    public class AskUserToolV2 : IAutoCreateToolV2, IToolV3
    {
        /// <summary>选项上限：防止模型把整篇文章塞进单选列表。</summary>
        private const int MaxOptions = 12;

        /// <summary>超时上限（秒）。模型可以指定等待多久，但不能要求等上一整天。</summary>
        private const int MaxTimeoutSeconds = 3600;

        private readonly PersistentEnv _env;

        public static string NameKey => "tool.ask_user";
        public static string DescriptionKey => "tool.ask_user.desc";
        public static string[] CategoryKeys => ["category.tool", "category.general"];

        public AskUserToolV2(PersistentEnv env)
        {
            _env = env;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new AskUserToolV2(env);

        public Tool GetDefinition() => new()
        {
            name = "ask_user",
            description =
                "Ask the user a question and wait for their answer. Use it when you need a decision, " +
                "a preference, or information only the user can provide. The user may answer, skip the " +
                "question, or the wait may time out — the returned status tells you which happened, " +
                "and it is authoritative: never assume an answer that was not returned.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "question",
                    type = ParameterType.String,
                    description = "The question to show the user. Keep it short and specific."
                }, true),
                (new ToolParameter
                {
                    name = "options",
                    type = ParameterType.Array,
                    description = "Optional candidate answers rendered as a single-select list. " +
                                  "Leave empty when the answer should be free text."
                }, false),
                (new ToolParameter
                {
                    name = "allow_free_text",
                    type = ParameterType.Boolean,
                    description = "When options are given, also allow the user to type a custom answer."
                }, false),
                (new ToolParameter
                {
                    name = "timeout_seconds",
                    type = ParameterType.Number,
                    description = "How long to wait before giving up, in seconds. " +
                                  "0 or omitted waits until the user answers or the generation is stopped. " +
                                  $"Maximum {MaxTimeoutSeconds}. On timeout the result status is 'timed_out' — " +
                                  "continue without the user's input."
                }, false)
            ]
        };

        public async Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            var interactions = _env.Interactions;
            if (interactions == null)
            {
                // 虚拟会话（子代理）没有界面可展示问题：明确失败，让模型改用文字提问
                _env.DebugOutput.Write("AskUserTool", "Interaction",
                    $"Chat {_env.ChatGuid}: ask_user invoked without an interaction channel; returning unavailable.");
                return ToolResult.Fail(ToolErrorCodes.InteractionUnavailable,
                    "This context cannot show questions to a user. Ask the question in plain text instead.");
            }

            if (!context.TryGetArguments<AskUserArguments>(out var args, out var argumentError) || args == null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, argumentError ?? "Invalid arguments.");

            var question = args.question?.Trim();
            if (string.IsNullOrWhiteSpace(question))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'question' is required.");

            var options = NormalizeOptions(args.options);

            var (timeout, timeoutError) = ResolveTimeout(args.timeout_seconds);
            if (timeoutError != null)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, timeoutError);

            var request = new ToolInteractionRequest
            {
                ToolName = "ask_user",
                Title = question,
                Form = ToolUiForm.Ask(question, options, args.allow_free_text)
            };

            // 无超时（默认）：等待的终点是"用户回答 / 跳过 / 生成被取消"三者之一。
            // 有超时：到点后返回 timed_out，本轮 loop 继续。
            var result = await interactions.RequestAsync(_env.ChatGuid, request, timeout, ct);

            _env.DebugOutput.Write("AskUserTool", "Interaction",
                $"Chat {_env.ChatGuid}: ask_user finished with '{result.Outcome}' " +
                $"(timeout: {(timeout is { } t ? $"{t.TotalSeconds:0}s" : "none")}).");

            return ToolResult.Ok(result.ToModelJson());
        }

        /// <summary>
        /// 解析模型给出的超时。0 / 缺省 = 无限等待；负数与超上限是明确的参数错误，
        /// 不做静默截断（截断会让模型以为自己的超时生效了）。
        /// </summary>
        private static (TimeSpan? Timeout, string? Error) ResolveTimeout(int? seconds)
        {
            if (seconds is null || seconds == 0)
                return (null, null);

            if (seconds < 0)
                return (null,
                    "'timeout_seconds' must be 0 (wait indefinitely) or a positive number of seconds.");

            if (seconds > MaxTimeoutSeconds)
                return (null, $"'timeout_seconds' must not exceed {MaxTimeoutSeconds}.");

            return (TimeSpan.FromSeconds(seconds.Value), null);
        }

        /// <summary>
        /// 把模型给的 options 归一化成字符串列表。允许"字符串数组"与"换行/逗号分隔的字符串"两种形态
        /// （部分模型不严格遵守 Array 类型），归一化行为会写进 DebugOutput，不做静默处理。
        /// </summary>
        private IReadOnlyList<string> NormalizeOptions(JToken? raw)
        {
            if (raw == null || raw.Type == JTokenType.Null)
                return Array.Empty<string>();

            IEnumerable<string> items;
            switch (raw.Type)
            {
                case JTokenType.Array:
                    items = raw.Children().Select(c => c.Type == JTokenType.String ? c.Value<string>()! : c.ToString());
                    break;

                case JTokenType.String:
                    var text = raw.Value<string>() ?? string.Empty;
                    items = text.Split(['\n', '\r', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries);
                    _env.DebugOutput.Write("AskUserTool", "Interaction",
                        "ask_user: 'options' was a string, split into a list. Models should pass an array.");
                    break;

                default:
                    items = [raw.ToString()];
                    _env.DebugOutput.Write("AskUserTool", "Interaction",
                        $"ask_user: 'options' had unexpected type '{raw.Type}', coerced to a single option.");
                    break;
            }

            var result = new List<string>();
            foreach (var item in items)
            {
                var option = item.Trim();
                if (option.Length == 0) continue;
                if (result.Contains(option, StringComparer.Ordinal)) continue;

                result.Add(option);
                if (result.Count >= MaxOptions)
                {
                    _env.DebugOutput.Write("AskUserTool", "Interaction",
                        $"ask_user: options truncated to {MaxOptions} items.");
                    break;
                }
            }

            return result;
        }

        private sealed class AskUserArguments
        {
            public string? question { get; set; }
            public JToken? options { get; set; }
            public bool allow_free_text { get; set; }

            /// <summary>等待秒数；0 或缺省表示一直等到用户回答。</summary>
            public int? timeout_seconds { get; set; }
        }
    }
}
