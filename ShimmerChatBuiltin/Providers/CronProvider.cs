using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Providers
{
    /// <summary>
    /// CRON 定时生成提供器（内置验证示例）。
    ///
    /// 按标准 5 字段 CRON 表达式（分 时 日 月 周）定时触发一次生成：
    /// 宿主创建带来源标记的 Chat、写入触发消息，并以提供器声明的
    /// 管线树参数（见 GetPipelineOverrides）执行生成。
    /// 支持语法：*、数值、a-b 区间、逗号列表、/step 步进；日与周几遵循标准 OR 语义。
    /// </summary>
    [ProviderInfo("provider.cron.name", DescriptionKey = "provider.cron.desc", Icon = "⏰")]
    public class CronProvider : IGenerationProvider
    {
        /// <summary>CRON 表达式（5 字段：分 时 日 月 周），如 "0 9 * * *" 表示每天 9:00</summary>
        [NodeProperty("provider.cron.expr", HintKey = "provider.cron.expr_hint", Order = 0)]
        public string CronExpression { get; set; } = "0 * * * *";

        /// <summary>触发时写入 Chat 的消息文本（以 System 身份，留空则不写入）</summary>
        [NodeProperty("provider.cron.trigger_text", HintKey = "provider.cron.trigger_text_hint",
            Order = 1, MultiLine = true)]
        public string TriggerText { get; set; } = "";

        // ─── 管线树参数：由提供器自行声明，null = 继承 Agent ───

        [PipelineTreeProperty("provider.cron.pre_tree", ProviderPipelineKind.PreGeneration, Order = 2)]
        public string? PreGenerationTreeJson { get; set; }

        [PipelineTreeProperty("provider.cron.post_tree", ProviderPipelineKind.PostGeneration, Order = 3)]
        public string? PostGenerationTreeJson { get; set; }

        [PipelineTreeProperty("provider.cron.render_tree", ProviderPipelineKind.RenderModifier, Order = 4)]
        public string? RenderModifierTreeJson { get; set; }

        private CancellationTokenSource? _cts;
        private Task? _loop;

        public Task StartAsync(ProviderRuntimeContext runtime, CancellationToken ct = default)
        {
            // 配置非法时抛出异常，宿主会记录 START_ERROR 并保留事件
            var schedule = CronSchedule.Parse(CronExpression);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(async () =>
            {
                var next = schedule.GetNextOccurrence(DateTime.Now);
                while (!token.IsCancellationRequested)
                {
                    // 分段等待，规避 Task.Delay 的 ~24.8 天上限
                    while (true)
                    {
                        var delay = next - DateTime.Now;
                        if (delay <= TimeSpan.Zero) break;
                        var step = delay > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : delay;
                        try { await Task.Delay(step, token); }
                        catch (OperationCanceledException) { return; }
                    }

                    if (token.IsCancellationRequested) break;

                    try
                    {
                        var text = string.IsNullOrWhiteSpace(TriggerText) ? null : TriggerText;
                        // overrides 传 null：由宿主回调 GetPipelineOverrides() 拉取
                        await runtime.TriggerAsync(text, overrides: null, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        // 单次生成失败不中断调度，错误落盘 DebugOutput
                        runtime.DebugOutput.Write("CronProvider", "error",
                            $"[{runtime.AgentGuid}/{runtime.Event.Id}] Cron generation failed: {ex.Message}");
                    }

                    next = schedule.GetNextOccurrence(DateTime.Now);
                }
            }, token);

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_loop != null)
            {
                try { await _loop; }
                catch (OperationCanceledException) { }
            }
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }

        public string? GetManualTriggerText()
            => string.IsNullOrWhiteSpace(TriggerText) ? null : TriggerText;

        /// <summary>定时触发与手动触发共用的管线树覆盖（未配置的树继承 Agent）</summary>
        public PipelineTreeOverrides? GetPipelineOverrides()
            => new()
            {
                PreGenerationTreeJson = PreGenerationTreeJson,
                PostGenerationTreeJson = PostGenerationTreeJson,
                RenderModifierTreeJson = RenderModifierTreeJson
            };
    }

    /// <summary>
    /// 轻量 CRON 调度解析器。支持 5 字段标准表达式：
    /// *、单值、a-b 区间、逗号列表、/step 步进；周几字段 0/7 均表示周日。
    /// </summary>
    internal class CronSchedule
    {
        private readonly HashSet<int> _minutes;
        private readonly HashSet<int> _hours;
        private readonly HashSet<int> _days;
        private readonly HashSet<int> _months;
        private readonly HashSet<int> _weekdays;
        private readonly bool _dayWild;
        private readonly bool _weekdayWild;

        private CronSchedule(HashSet<int> minutes, HashSet<int> hours, HashSet<int> days,
            HashSet<int> months, HashSet<int> weekdays, bool dayWild, bool weekdayWild)
        {
            _minutes = minutes;
            _hours = hours;
            _days = days;
            _months = months;
            _weekdays = weekdays;
            _dayWild = dayWild;
            _weekdayWild = weekdayWild;
        }

        public static CronSchedule Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new FormatException("Empty cron expression.");

            var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
                throw new FormatException($"Cron expression must have 5 fields, got {parts.Length}: '{expression}'.");

            var minutes = ParseField(parts[0], 0, 59, out _);
            var hours = ParseField(parts[1], 0, 23, out _);
            var days = ParseField(parts[2], 1, 31, out var dayWild);
            var months = ParseField(parts[3], 1, 12, out _);
            var weekdays = ParseField(parts[4], 0, 7, out var weekdayWild);
            if (weekdays.Remove(7))
                weekdays.Add(0); // 7 与 0 都表示周日

            return new CronSchedule(minutes, hours, days, months, weekdays, dayWild, weekdayWild);
        }

        /// <summary>求 strictly after 给定时间的下一次触发时刻（最长搜索一年）</summary>
        public DateTime GetNextOccurrence(DateTime after)
        {
            var t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0)
                .AddMinutes(1);
            const int maxMinutesPerYear = 366 * 24 * 60;
            for (var i = 0; i < maxMinutesPerYear; i++)
            {
                if (Matches(t))
                    return t;
                t = t.AddMinutes(1);
            }
            throw new InvalidOperationException($"No cron occurrence within one year: '{this}'.");
        }

        private bool Matches(DateTime t)
        {
            if (!_minutes.Contains(t.Minute)) return false;
            if (!_hours.Contains(t.Hour)) return false;
            if (!_months.Contains(t.Month)) return false;

            var dayOk = _days.Contains(t.Day);
            var weekdayOk = _weekdays.Contains((int)t.DayOfWeek);

            // 标准 CRON 语义：日与周几同时受限时取 OR，任一为 * 时仅看另一方
            if (_dayWild && _weekdayWild) return true;
            if (_dayWild) return weekdayOk;
            if (_weekdayWild) return dayOk;
            return dayOk || weekdayOk;
        }

        private static HashSet<int> ParseField(string field, int min, int max, out bool wild)
        {
            wild = field == "*";
            var set = new HashSet<int>();

            foreach (var raw in field.Split(','))
            {
                var part = raw.Trim();
                var step = 1;

                var stepIdx = part.IndexOf('/');
                if (stepIdx >= 0)
                {
                    if (!int.TryParse(part[(stepIdx + 1)..], out step) || step <= 0)
                        throw new FormatException($"Invalid cron step: '{raw}'.");
                    part = part[..stepIdx];
                    if (part == "*")
                        part = $"{min}-{max}";
                }

                int lo, hi;
                var dashIdx = part.IndexOf('-');
                if (dashIdx >= 0)
                {
                    if (!int.TryParse(part[..dashIdx], out lo) || !int.TryParse(part[(dashIdx + 1)..], out hi))
                        throw new FormatException($"Invalid cron range: '{raw}'.");
                }
                else if (part == "*")
                {
                    lo = min;
                    hi = max;
                }
                else
                {
                    if (!int.TryParse(part, out lo))
                        throw new FormatException($"Invalid cron field: '{raw}'.");
                    hi = lo;
                }

                if (lo < min || hi > max || lo > hi)
                    throw new FormatException($"Cron field out of range [{min},{max}]: '{raw}'.");

                for (var v = lo; v <= hi; v += step)
                    set.Add(v);
            }

            if (set.Count == 0)
                throw new FormatException($"Empty cron field: '{field}'.");
            return set;
        }
    }
}
