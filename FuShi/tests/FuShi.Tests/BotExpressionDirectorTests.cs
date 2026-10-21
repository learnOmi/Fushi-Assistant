using FluentAssertions;
using FuShi.Domain.Assistant;
using FuShi.Domain.Enums;

namespace FuShi.Tests;

/// <summary>
/// 表情状态机的单测：课程状态优先于待机氛围、临期与课间的窗口边界、提前量为 0 的关闭语义。
/// <para>
/// 所有输入直接给值，不读系统时钟；时间统一用分钟数表达，便于把边界写在用例里。
/// </para>
/// </summary>
public sealed class BotExpressionDirectorTests
{
    /// <summary>测试统一使用的提前量（分钟），与设置页默认一致。</summary>
    private const int LEAD_MINUTES = 5;

    [Fact]
    public void InClass_ShouldFocus_WhenLessonIsInProgress()
    {
        Resolve(Signals(inClass: true, status: OccurrenceStatus.InProgress))
            .Should().Be(BotExpression.Focus);
    }

    [Fact]
    public void InClass_ShouldFocus_WhenNoLedgerRowExists()
    {
        Resolve(Signals(inClass: true, status: null)).Should().Be(BotExpression.Focus);
    }

    [Fact]
    public void InClass_ShouldDone_WhenLessonCompleted()
    {
        Resolve(Signals(inClass: true, status: OccurrenceStatus.Done))
            .Should().Be(BotExpression.Done);
    }

    [Fact]
    public void InClass_ShouldLate_WhenReminderFiredButNotStarted()
    {
        Resolve(Signals(inClass: true, status: OccurrenceStatus.Fired))
            .Should().Be(BotExpression.Late);
    }

    [Fact]
    public void InClass_ShouldLate_WhenMarkedMissed()
    {
        Resolve(Signals(inClass: true, status: OccurrenceStatus.Missed))
            .Should().Be(BotExpression.Late);
    }

    [Fact]
    public void InClass_ShouldKeepFocus_EvenWhenIdleForHours()
    {
        Resolve(Signals(
                inClass: true,
                status: OccurrenceStatus.InProgress,
                idleFor: TimeSpan.FromHours(3)))
            .Should().Be(BotExpression.Focus,
                "困倦是待机氛围，不能覆盖正在发生的课程");
    }

    [Fact]
    public void ShouldSoon_WhenNextLessonIsInsideLeadWindow()
    {
        Resolve(Signals(minutesUntilNext: 3)).Should().Be(BotExpression.Soon);
    }

    [Fact]
    public void ShouldSoon_OnLeadWindowBoundary()
    {
        Resolve(Signals(minutesUntilNext: LEAD_MINUTES)).Should().Be(BotExpression.Soon);
    }

    [Fact]
    public void ShouldIdle_WhenNextLessonIsOutsideLeadWindow()
    {
        Resolve(Signals(minutesUntilNext: LEAD_MINUTES + 1)).Should().Be(BotExpression.Idle);
    }

    [Fact]
    public void ShouldNotSoon_WhenLeadTimeIsZero()
    {
        var expression = BotExpressionDirector.ResolveSteady(
            Signals(minutesUntilNext: 0), leadMinutes: 0);

        expression.Should().Be(BotExpression.Idle,
            "提前量为 0 表示不提前提醒，临期表情同步关闭");
    }

    [Fact]
    public void ShouldBreak_WhenJustFinishedAndMoreLessonsToday()
    {
        Resolve(Signals(minutesSinceLastEnd: 8, hasFollowingToday: true))
            .Should().Be(BotExpression.Break);
    }

    [Fact]
    public void ShouldBreak_OnBreakWindowBoundary()
    {
        Resolve(Signals(minutesSinceLastEnd: 10, hasFollowingToday: true))
            .Should().Be(BotExpression.Break);
    }

    [Fact]
    public void ShouldIdle_WhenBreakWindowPassed()
    {
        Resolve(Signals(minutesSinceLastEnd: 11, hasFollowingToday: true))
            .Should().Be(BotExpression.Idle);
    }

    [Fact]
    public void ShouldIdle_WhenDayIsOverAfterLastLesson()
    {
        Resolve(Signals(minutesSinceLastEnd: 3, hasFollowingToday: false))
            .Should().Be(BotExpression.Idle);
    }

    [Fact]
    public void ShouldSoon_TakePriorityOverBreak()
    {
        Resolve(Signals(
                minutesUntilNext: 2,
                minutesSinceLastEnd: 5,
                hasFollowingToday: true))
            .Should().Be(BotExpression.Soon,
                "下一节已经进入提前量窗口时，临期比课间更重要");
    }

    [Fact]
    public void ShouldSleepy_AfterTwoHoursWithoutInteraction()
    {
        Resolve(Signals(idleFor: TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1)))
            .Should().Be(BotExpression.Sleepy);
    }

    [Fact]
    public void ShouldIdle_JustBeforeSleepyThreshold()
    {
        Resolve(Signals(idleFor: TimeSpan.FromHours(2) - TimeSpan.FromMinutes(1)))
            .Should().Be(BotExpression.Idle);
    }

    [Fact]
    public void ShouldBreak_TakePriorityOverSleepy()
    {
        Resolve(Signals(
                minutesSinceLastEnd: 5,
                hasFollowingToday: true,
                idleFor: TimeSpan.FromHours(3)))
            .Should().Be(BotExpression.Break,
                "刚下课的中场休息比长期闲置更具体");
    }

    [Fact]
    public void ConflictSignal_ShouldNotAffectSteadyExpression()
    {
        Resolve(Signals(hasConflict: true)).Should().Be(BotExpression.Idle,
            "冲突是一次性瞬态，由视图模型检测上升沿后发出，不进稳态");
    }

    [Fact]
    public void AllDoneSignal_ShouldNotAffectSteadyExpression()
    {
        Resolve(Signals(
                inClass: true,
                status: OccurrenceStatus.Done,
                allDoneToday: true))
            .Should().Be(BotExpression.Done,
                "全勤庆祝是一次性瞬态，稳态仍是本节的完成态");
    }

    /// <summary>用统一的提前量调用状态机。</summary>
    private static BotExpression Resolve(BotSignals signals) =>
        BotExpressionDirector.ResolveSteady(signals, LEAD_MINUTES);

    /// <summary>构造信号；未指定的字段取「没有课程、没有交互」的中性值。</summary>
    private static BotSignals Signals(
        bool inClass = false,
        OccurrenceStatus? status = null,
        int? minutesUntilNext = null,
        int? minutesSinceLastEnd = null,
        bool hasFollowingToday = false,
        bool hasConflict = false,
        bool allDoneToday = false,
        bool allDoneForSevenDays = false,
        TimeSpan? idleFor = null) =>
        new(
            inClass,
            status,
            minutesUntilNext,
            minutesSinceLastEnd,
            hasFollowingToday,
            hasConflict,
            allDoneToday,
            allDoneForSevenDays,
            idleFor ?? TimeSpan.Zero);
}
