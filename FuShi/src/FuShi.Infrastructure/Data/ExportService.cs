using System.Globalization;
using System.IO;
using System.Text;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FuShi.Infrastructure.Data;

/// <summary>
/// CSV 导出结果（M5a）。失败同样不抛异常，原因经 <see cref="Error"/> 透出。
/// </summary>
/// <param name="Success">是否两份文件都导出成功。</param>
/// <param name="SchedulePath">课表 CSV 路径；失败为 null。</param>
/// <param name="TaskPath">事项目 CSV 路径；失败为 null。</param>
/// <param name="Error">失败原因；成功为 null。</param>
public sealed record ExportResult(bool Success, string? SchedulePath, string? TaskPath, string? Error)
{
    /// <summary>成功结果。</summary>
    /// <param name="schedulePath">课表 CSV 路径。</param>
    /// <param name="taskPath">事项 CSV 路径。</param>
    public static ExportResult Ok(string schedulePath, string taskPath) =>
        new(true, schedulePath, taskPath, null);

    /// <summary>失败结果。</summary>
    /// <param name="error">失败原因。</param>
    public static ExportResult Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// CSV 导出服务（M5a）：把「当前生效阶段的课表」与「全部事项」各导出一份 UTF-8（带 BOM）CSV，
/// 便于用户在 Excel 里查看或自行加工。本阶段只导出、不导入。
/// <para>
/// 编码必须带 BOM：Excel 双击打开无 BOM 的 UTF-8 CSV 会把中文显示成乱码。
/// 单元格转义统一走 <see cref="Csv"/>（纯函数，可单测）。
/// </para>
/// </summary>
public sealed class ExportService
{
    /// <summary>带 BOM 的 UTF-8（Excel 兼容的唯一正确选择）。</summary>
    private static readonly Encoding UTF8_WITH_BOM = new UTF8Encoding(true);

    /// <summary>导出的日期时间格式（截止时刻）。</summary>
    private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm";

    /// <summary>导出的时刻格式。</summary>
    private const string CLOCK_FORMAT = "HH:mm";

    /// <summary>一周天数（周几标签取模用）。</summary>
    private const int DAYS_PER_WEEK = 7;

    /// <summary>DayOfWeek 的周日 = 0 到「周一打头」下标的偏移。</summary>
    private const int DAYS_FROM_SUNDAY_TO_MONDAY = 6;

    private readonly IDbContextFactory<FuShiDbContext> _contextFactory;
    private readonly ILogger _logger;
    private readonly string _exportDirectory;

    /// <summary>构造导出服务（使用默认导出目录 %APPDATA%\FuShi\export）。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public ExportService(IDbContextFactory<FuShiDbContext> contextFactory, ILogger logger)
        : this(contextFactory, logger, StoragePaths.ExportDirectory)
    {
    }

    /// <summary>构造导出服务并指定导出目录（测试用）。</summary>
    /// <param name="contextFactory">上下文工厂。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="exportDirectory">导出目录绝对路径。</param>
    public ExportService(
        IDbContextFactory<FuShiDbContext> contextFactory,
        ILogger logger,
        string exportDirectory)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _exportDirectory = exportDirectory;
    }

    /// <summary>导出目录绝对路径（设置页展示用）。</summary>
    public string ExportDirectory => _exportDirectory;

    /// <summary>
    /// 导出两份 CSV：课表（当前生效阶段全部模板）与事项（全部事项）。
    /// 任一文件写入失败即返回失败结果，已写出的文件保留（用户至少能拿到一部分）。
    /// </summary>
    /// <param name="now">当前时刻（决定文件名中的时间戳）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<ExportResult> ExportAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        string? schedulePath = null;

        try
        {
            Directory.CreateDirectory(_exportDirectory);

            schedulePath = FileNaming.EnsureUnique(Path.Combine(
                _exportDirectory,
                string.Format(CultureInfo.InvariantCulture, ExportConstants.SCHEDULE_FILE_FORMAT, now)));

            var taskPath = FileNaming.EnsureUnique(Path.Combine(
                _exportDirectory,
                string.Format(CultureInfo.InvariantCulture, ExportConstants.TASKS_FILE_FORMAT, now)));

            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var scheduleCsv = await BuildScheduleCsvAsync(context, cancellationToken).ConfigureAwait(false);
            var taskCsv = await BuildTaskCsvAsync(context, cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(schedulePath, scheduleCsv, UTF8_WITH_BOM, cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(taskPath, taskCsv, UTF8_WITH_BOM, cancellationToken)
                .ConfigureAwait(false);

            _logger.Information("CSV 导出完成：{SchedulePath} / {TaskPath}", schedulePath, taskPath);
            return ExportResult.Ok(schedulePath, taskPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "CSV 导出失败：{Directory}", _exportDirectory);
            return ExportResult.Fail(ex.Message);
        }
    }

    /// <summary>构建课表 CSV：当前生效阶段的全部条目（含停用，便于用户核对），按周几与开始时刻排序。</summary>
    /// <param name="context">短生命周期上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<string> BuildScheduleCsvAsync(
        FuShiDbContext context,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(Csv.Line(ExportConstants.SCHEDULE_HEADERS));

        var phase = await context.Phases
            .Where(static phase => phase.IsActive)
            .OrderByDescending(static phase => phase.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (phase is null)
        {
            return builder.ToString();
        }

        var rows = await (from entry in context.ScheduleEntries
                          join subject in context.Subjects on entry.SubjectId equals subject.Id
                          where entry.PhaseId == phase.Id
                          orderby entry.DayOfWeek, entry.StartTime
                          select new
                          {
                              entry.DayOfWeek,
                              entry.StartTime,
                              entry.EndTime,
                              SubjectName = subject.Name,
                              subject.Category,
                              entry.Note,
                              entry.Enabled,
                          })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            builder.Append(Csv.Line(
                WeekdayLabel(row.DayOfWeek),
                row.StartTime.ToString(CLOCK_FORMAT, CultureInfo.InvariantCulture),
                row.EndTime.ToString(CLOCK_FORMAT, CultureInfo.InvariantCulture),
                row.SubjectName,
                CategoryLabel(row.Category),
                row.Note,
                row.Enabled ? ExportConstants.ENABLED_YES : ExportConstants.ENABLED_NO));
        }

        return builder.ToString();
    }

    /// <summary>构建事项 CSV：全部事项（不按日期裁剪，导出即全量），按截止/优先级排序。</summary>
    /// <param name="context">短生命周期上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task<string> BuildTaskCsvAsync(
        FuShiDbContext context,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append(Csv.Line(ExportConstants.TASK_HEADERS));

        var tasks = await context.Tasks
            .OrderBy(static task => task.DueAt == null)
            .ThenBy(static task => task.DueAt)
            .ThenByDescending(static task => task.Priority)
            .ThenBy(static task => task.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var task in tasks)
        {
            builder.Append(Csv.Line(
                task.Title,
                task.DueAt?.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture)
                    ?? ExportConstants.EMPTY_VALUE,
                PriorityLabel(task.Priority),
                RepeatLabel(task.RepeatRule),
                StatusLabel(task.Status)));
        }

        return builder.ToString();
    }

    /// <summary>周几标签（周日 = 0 映射到「周一打头」的下标）。</summary>
    /// <param name="day">星期几。</param>
    private static string WeekdayLabel(DayOfWeek day) =>
        ExportConstants.WEEKDAY_LABELS[((int)day + DAYS_FROM_SUNDAY_TO_MONDAY) % DAYS_PER_WEEK];

    /// <summary>科目分类标签；越界值回落「学习」，避免脏数据把导出打断。</summary>
    /// <param name="category">分类枚举。</param>
    private static string CategoryLabel(SubjectCategory category)
    {
        var index = (int)category;
        return index >= 0 && index < ExportConstants.CATEGORY_LABELS.Length
            ? ExportConstants.CATEGORY_LABELS[index]
            : ExportConstants.CATEGORY_LABELS[0];
    }

    /// <summary>优先级标签。</summary>
    /// <param name="priority">优先级。</param>
    private static string PriorityLabel(TaskPriority priority) => priority switch
    {
        TaskPriority.High => ExportConstants.PRIORITY_HIGH,
        TaskPriority.Low => ExportConstants.PRIORITY_LOW,
        _ => ExportConstants.PRIORITY_NORMAL,
    };

    /// <summary>重复规则标签（受控三档，未知按不重复）。</summary>
    /// <param name="rule">原始规则文本。</param>
    private static string RepeatLabel(string? rule) => TaskRecurrenceExpander.NormalizeRule(rule) switch
    {
        TaskRecurrenceExpander.RULE_DAILY => ExportConstants.REPEAT_DAILY,
        TaskRecurrenceExpander.RULE_WEEKLY => ExportConstants.REPEAT_WEEKLY,
        _ => ExportConstants.REPEAT_NONE,
    };

    /// <summary>事项状态标签。</summary>
    /// <param name="status">状态枚举。</param>
    private static string StatusLabel(TaskItemStatus status) => status switch
    {
        TaskItemStatus.Done => ExportConstants.STATUS_DONE,
        TaskItemStatus.Skipped => ExportConstants.STATUS_SKIPPED,
        _ => ExportConstants.STATUS_PENDING,
    };
}
