using ShimmerChatBuiltin.Providers;

namespace ShimmerChatBuiltin.Tests.Providers;

public class CronScheduleTests
{
    [Fact]
    public void EveryMinute_ReturnsNextMinute()
    {
        var schedule = CronSchedule.Parse("* * * * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 10, 30, 45))
            .Should().Be(new DateTime(2026, 1, 1, 10, 31, 0));
    }

    [Fact]
    public void DailyAtFixedHour_ReturnsTodayWhenNotPassed()
    {
        var schedule = CronSchedule.Parse("0 9 * * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 8, 0, 0))
            .Should().Be(new DateTime(2026, 1, 1, 9, 0, 0));
    }

    [Fact]
    public void DailyAtFixedHour_ReturnsNextDayWhenPassed()
    {
        var schedule = CronSchedule.Parse("0 9 * * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 9, 30, 0))
            .Should().Be(new DateTime(2026, 1, 2, 9, 0, 0));
    }

    [Fact]
    public void StepOnlyExpression_IsTreatedAsFullRangeStep()
    {
        // "*/30 * * * *" 在本地化提示中明示支持，必须能解析
        var schedule = CronSchedule.Parse("*/30 * * * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 10, 7, 0))
            .Should().Be(new DateTime(2026, 1, 1, 10, 30, 0));
    }

    [Fact]
    public void RangeWithStep_Works()
    {
        var schedule = CronSchedule.Parse("0-30/10 * * * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 10, 35, 0))
            .Should().Be(new DateTime(2026, 1, 1, 11, 0, 0));
    }

    [Fact]
    public void CommaList_Works()
    {
        var schedule = CronSchedule.Parse("0 12 1,15 * *");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2026, 1, 1, 12, 0, 0));
    }

    [Fact]
    public void WeekdayOnly_ReturnsNextMatchingWeekday()
    {
        // 2026-01-01 是周四；下一个周一为 2026-01-05
        var schedule = CronSchedule.Parse("0 0 * * 1");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2026, 1, 5, 0, 0, 0));
    }

    [Fact]
    public void Sunday_IsZeroAndSeven()
    {
        // 2026-01-04 是周日
        var zero = CronSchedule.Parse("0 0 * * 0");
        var seven = CronSchedule.Parse("0 0 * * 7");

        zero.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2026, 1, 4, 0, 0, 0));
        seven.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2026, 1, 4, 0, 0, 0));
    }

    [Fact]
    public void DayAndWeekday_Restricted_UseOrSemantics()
    {
        // 每月 1 日 或 周一；2026-01-01（周四，已是 1 日但时刻已过），
        // 最近匹配应为 2026-01-05（周一）00:00
        var schedule = CronSchedule.Parse("0 0 1 * 1");
        schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2026, 1, 5, 0, 0, 0));
    }

    [Fact]
    public void LeapDay_ReturnsNextLeapYearOccurrence()
    {
        var schedule = CronSchedule.Parse("0 0 29 2 *");
        schedule.GetNextOccurrence(new DateTime(2028, 1, 1, 0, 0, 0))
            .Should().Be(new DateTime(2028, 2, 29, 0, 0, 0));
    }

    [Fact]
    public void LeapDay_NoOccurrenceWithinOneYear_Throws()
    {
        // 2028-03-01 之后下一个 2 月 29 日是 2032 年，超出一年搜索窗口
        var schedule = CronSchedule.Parse("0 0 29 2 *");
        var act = () => schedule.GetNextOccurrence(new DateTime(2028, 3, 1, 0, 0, 0));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImpossibleDate_NoOccurrenceWithinOneYear_Throws()
    {
        // 2 月 30 日不存在，全年无匹配
        var schedule = CronSchedule.Parse("0 0 30 2 *");
        var act = () => schedule.GetNextOccurrence(new DateTime(2026, 1, 1, 0, 0, 0));
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 9 * *")]          // 只有 4 个字段
    [InlineData("0 9 * * * *")]      // 6 个字段
    [InlineData("60 * * * *")]       // 分钟越界
    [InlineData("0 24 * * *")]       // 小时越界
    [InlineData("0 0 0 * *")]        // 日期越界
    [InlineData("0 0 * 13 *")]       // 月份越界
    [InlineData("0 0 * * 8")]        // 周几越界
    [InlineData("*/0 * * * *")]      // 步进为 0
    [InlineData("*/a * * * *")]      // 步进非数字
    [InlineData("5-2 * * * *")]      // 区间起点大于终点
    [InlineData("a * * * *")]        // 非数字
    public void Parse_InvalidExpression_ThrowsFormatException(string expression)
    {
        var act = () => CronSchedule.Parse(expression);
        act.Should().Throw<FormatException>();
    }
}
