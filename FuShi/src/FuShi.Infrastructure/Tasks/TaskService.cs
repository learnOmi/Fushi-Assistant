using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Tasks;

/// <summary>
/// 事项写服务（M4c）：CRUD、完成/跳过、按区间取列表。
/// <para>
/// 与 <see cref="ScheduleService"/> 的分工：本服务只改「模板」（<see cref="TaskItem"/>）与完成记录，
/// 每次写操作后调用 <see cref="ScheduleService.ReconcileTaskAsync"/> 重建该事项的未来实例——
/// 与课表对账同一口径：<b>改模板只影响未来，已发生的账本不改写</b>。
/// </para>
/// <para>
/// 每次操作都用工厂创建<b>短生命周期</b>上下文；内部 await 一律 <c>ConfigureAwait(false)</c>，
/// 使 UI 线程可 <c>GetAwaiter().GetResult()</c> 同步等待而不死锁。
/// </para>
/// </summary>
public sealed class TaskService
{
    /// <summary>完成记录中事项侧的占位时长：事项没有计划时长，也没有实际耗时语义。</summary>
    private const int TASK_MINUTES = 0;

    /// <summary>标题为空时的校验提示。</summary>
    private const string ERROR_TITLE_REQUIRED = "事项标题不能为空";

    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ScheduleService _schedule;
    private readonly ILogger _logger;

    /// <summary>构造事项服务。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="schedule">调度服务（写后对账）。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public TaskService(
        IDbContextFactory<FuShiDbContext> contextFactory,
        ScheduleService schedule,
        ILogger logger)
    {
        _contextFactory = contextFactory;
        _schedule = schedule;
        _logger = logger;
    }

    /// <summary>
    /// 按日期区间取事项列表：区间内的事项 + 所有「早于区间且仍未待办」的事项（逾期永不消失）+ 无截止事项。
    /// 已完成的旧事项只保留区间内的，避免列表无限增长。
    /// </summary>
    /// <param name="from">区间起（含，按日期）。</param>
    /// <param name="to">区间止（含，按日期）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rangeStart = from.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await context.Tasks
            .Where(t => t.DueAt == null
                        || (t.DueAt >= rangeStart && t.DueAt < rangeEnd)
                        || (t.Status == TaskItemStatus.Pending && t.DueAt < rangeStart))
            .OrderBy(t => t.DueAt == null)
            .ThenBy(t => t.DueAt)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Title)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>新建事项并立即对账，使其未来实例马上落账（无需等引擎下一轮展开）。</summary>
    /// <param name="task">待创建事项（Id 会被忽略）。</param>
    /// <param name="now">当前时刻（对账分界线）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<TaskItem> CreateAsync(
        TaskItem task,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        Validate(task);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = new TaskItem
        {
            Title = task.Title.Trim(),
            DueAt = task.DueAt,
            RepeatRule = TaskRecurrenceExpander.NormalizeRule(task.RepeatRule),
            Priority = task.Priority,
            Status = TaskItemStatus.Pending,
        };

        context.Tasks.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("新建事项：{TaskId}「{Title}」@{DueAt}", entity.Id, entity.Title, entity.DueAt);

        await _schedule.ReconcileTaskAsync(entity.Id, now, cancellationToken).ConfigureAwait(false);
        return entity;
    }

    /// <summary>更新事项模板（标题/截止/重复/优先级）并对账未来实例；事项不存在返回 false。</summary>
    /// <param name="task">含 Id 与最新字段的事项。</param>
    /// <param name="now">当前时刻（对账分界线）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> UpdateAsync(
        TaskItem task,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        Validate(task);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == task.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        entity.Title = task.Title.Trim();
        entity.DueAt = task.DueAt;
        entity.RepeatRule = TaskRecurrenceExpander.NormalizeRule(task.RepeatRule);
        entity.Priority = task.Priority;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("更新事项：{TaskId}「{Title}」@{DueAt}", entity.Id, entity.Title, entity.DueAt);

        await _schedule.ReconcileTaskAsync(entity.Id, now, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>删除事项及其未来待触发实例与提醒规则；历史完成记录保留。</summary>
    /// <param name="taskId">事项主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<bool> DeleteAsync(int taskId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var removed = await context.Tasks
            .Where(t => t.Id == taskId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (removed == 0)
        {
            return false;
        }

        await context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == taskId
                        && o.Status == OccurrenceStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.ReminderRules
            .Where(r => r.OwnerType == OwnerType.Task && r.OwnerId == taskId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.Information("删除事项：{TaskId}", taskId);
        return true;
    }

    /// <summary>把事项标记为完成：同步当日实例状态、写一条完成记录、清掉未来实例。</summary>
    /// <param name="taskId">事项主键。</param>
    /// <param name="now">当前时刻（决定统计日期与"未来"分界）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<bool> MarkDoneAsync(int taskId, DateTime now, CancellationToken cancellationToken = default) =>
        SetStatusAsync(taskId, TaskItemStatus.Done, CompletionStatus.Done, now, cancellationToken);

    /// <summary>把事项标记为跳过：同步当日实例状态、写一条跳过记录、清掉未来实例。</summary>
    /// <param name="taskId">事项主键。</param>
    /// <param name="now">当前时刻（决定统计日期与"未来"分界）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<bool> MarkSkippedAsync(int taskId, DateTime now, CancellationToken cancellationToken = default) =>
        SetStatusAsync(taskId, TaskItemStatus.Skipped, CompletionStatus.Skipped, now, cancellationToken);

    /// <summary>
    /// 完成/跳过的公共实现。
    /// <list type="number">
    /// <item>只有 Pending 的事项可以流转（重复调用返回 false，避免重复落账）。</item>
    /// <item>优先把「今天」那条未终态实例置为对应终态；今天没有实例（无截止/当日未展开）则直接用今天作为统计日期。</item>
    /// <item>删除该事项的未来待触发实例，重复事项到此结束。</item>
    /// </list>
    /// </summary>
    private async Task<bool> SetStatusAsync(
        int taskId,
        TaskItemStatus taskStatus,
        CompletionStatus logStatus,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null || task.Status != TaskItemStatus.Pending)
        {
            return false;
        }

        var dayStart = now.Date;
        var dayEnd = dayStart.AddDays(1);

        var occurrence = await context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == taskId
                        && o.PlannedAt >= dayStart
                        && o.PlannedAt < dayEnd
                        && (o.Status == OccurrenceStatus.Pending
                            || o.Status == OccurrenceStatus.Fired
                            || o.Status == OccurrenceStatus.InProgress))
            .OrderBy(o => o.PlannedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurrenceStatus = taskStatus == TaskItemStatus.Done
            ? OccurrenceStatus.Done
            : OccurrenceStatus.Skipped;

        if (occurrence is not null)
        {
            occurrence.Status = occurrenceStatus;
        }

        task.Status = taskStatus;
        context.CompletionLogs.Add(new CompletionLog
        {
            Date = occurrence is null ? DateOnly.FromDateTime(now) : DateOnly.FromDateTime(occurrence.PlannedAt),
            EntryId = taskId,
            // 事项无科目、无时长：统计页归入「未分类」，只影响完成条数。
            SubjectId = null,
            PlannedMin = TASK_MINUTES,
            ActualMin = TASK_MINUTES,
            Status = logStatus,
        });

        // 未来实例不再提醒：保留今天这条（已置终态），删掉之后的 Pending。
        await context.Occurrences
            .Where(o => o.SourceType == OwnerType.Task
                        && o.SourceId == taskId
                        && o.Id != (occurrence == null ? 0 : occurrence.Id)
                        && o.Status == OccurrenceStatus.Pending
                        && (o.PlannedAt > now || o.ShiftMinutes > 0))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Information("事项状态流转：{TaskId} → {Status}", taskId, taskStatus);
        return true;
    }

    /// <summary>校验标题非空；空标题属于调用方（UI）漏校验，直接抛参数异常。</summary>
    private static void Validate(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            throw new ArgumentException(ERROR_TITLE_REQUIRED, nameof(task));
        }
    }
}
