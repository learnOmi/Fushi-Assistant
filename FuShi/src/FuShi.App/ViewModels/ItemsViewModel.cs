using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Infrastructure.Scheduling;
using FuShi.Infrastructure.Tasks;
using Serilog;

namespace FuShi.ViewModels;

/// <summary>
/// 事项页视图模型（M4c）：按截止日期分组展示事项，提供新增 / 编辑 / 完成 / 跳过。
/// <para>
/// 写操作一律走 <see cref="TaskService"/>（内部已完成对账），随后通知引擎重排休眠并刷新悬浮组件，
/// 使新建/改期后不必等下一轮轮询才生效。
/// </para>
/// <para>
/// 列表区间：今天 ~ 未来 7 天，外加所有「逾期未完成」与「无截止」事项——
/// 未完成的工作不应因为过了日期就从列表里消失。
/// </para>
/// </summary>
public sealed partial class ItemsViewModel : ObservableObject
{
    private readonly TaskService _taskService;
    private readonly ReminderEngine _engine;
    private readonly Action _reloadWidget;
    private readonly Func<TaskEditorViewModel, bool> _showEditor;
    private readonly ILogger _logger;

    /// <summary>当前列表对应的原始事项（编辑时按 Id 回查，避免再查一次库）。</summary>
    private IReadOnlyList<TaskItem> _tasks = Array.Empty<TaskItem>();

    /// <summary>分组后的展示行。</summary>
    [ObservableProperty]
    private IReadOnlyList<ItemGroupRow> _groups = Array.Empty<ItemGroupRow>();

    /// <summary>待办条数（页头统计）。</summary>
    [ObservableProperty]
    private int _pendingCount;

    /// <summary>加载失败提示（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否有错误。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>是否没有任何事项（驱动空态）。</summary>
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>页头待办计数文案。</summary>
    public string PendingCountText => string.Format(WindowText.ITEMS_COUNT_FORMAT, PendingCount);

    /// <summary>构造事项页视图模型。</summary>
    /// <param name="taskService">事项写服务。</param>
    /// <param name="engine">提醒引擎（写后请求重排休眠）。</param>
    /// <param name="reloadWidget">刷新悬浮组件的回调。</param>
    /// <param name="showEditor">弹出事项编辑对话框的委托，返回是否确定。</param>
    /// <param name="logger">日志器。</param>
    public ItemsViewModel(
        TaskService taskService,
        ReminderEngine engine,
        Action reloadWidget,
        Func<TaskEditorViewModel, bool> showEditor,
        ILogger logger)
    {
        _taskService = taskService;
        _engine = engine;
        _reloadWidget = reloadWidget;
        _showEditor = showEditor;
        _logger = logger;
    }

    /// <summary>重新载入事项列表（进入页签或窗口重新显示时调用）。</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            _tasks = await _taskService.GetTasksAsync(
                today,
                today.AddDays(TimeDefaults.EXPANSION_HORIZON_DAYS - 1));

            // 重复事项的模板永远是 Pending，行状态要看「今天这条实例」是否已终结（M5b）。
            var todayStatuses = await _taskService.GetTodayTaskStatusesAsync(today);

            Groups = BuildGroups(_tasks, today, todayStatuses);
            PendingCount = _tasks.Count(static task => task.Status == TaskItemStatus.Pending);
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            // 载入失败不吞异常：记日志并保留空态，避免页签切换把异常抛成进程崩溃。
            _logger.Error(ex, "加载事项列表失败");
            ErrorText = WindowText.ITEMS_LOAD_FAILED;
        }
    }

    /// <summary>新增事项：弹编辑框，确定后立即对账并刷新。</summary>
    [RelayCommand]
    private async Task AddItemAsync()
    {
        var viewModel = new TaskEditorViewModel(_taskService);
        viewModel.InitializeNew(DateTime.Now);

        if (!_showEditor(viewModel) || viewModel.SavedTask is null)
        {
            return;
        }

        await AfterChangedAsync();
    }

    /// <summary>编辑事项：弹编辑框（可删除），确定或删除后刷新。</summary>
    /// <param name="taskId">事项主键。</param>
    [RelayCommand]
    private async Task EditItemAsync(int taskId)
    {
        var task = _tasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
        {
            return;
        }

        var viewModel = new TaskEditorViewModel(_taskService);
        viewModel.InitializeExisting(task, DateTime.Now);

        if (!_showEditor(viewModel))
        {
            return;
        }

        if (viewModel.SavedTask is null && !viewModel.Deleted)
        {
            return;
        }

        await AfterChangedAsync();
    }

    /// <summary>标记完成：一次性事项结束系列，重复事项只完成今天这一次（M5b）。</summary>
    /// <param name="taskId">事项主键。</param>
    [RelayCommand]
    private async Task MarkDoneAsync(int taskId)
    {
        await _taskService.MarkDoneAsync(taskId, DateTime.Now);
        await AfterChangedAsync();
    }

    /// <summary>标记跳过：一次性事项结束系列，重复事项只跳过今天这一次（M5b）。</summary>
    /// <param name="taskId">事项主键。</param>
    [RelayCommand]
    private async Task MarkSkipAsync(int taskId)
    {
        await _taskService.MarkSkippedAsync(taskId, DateTime.Now);
        await AfterChangedAsync();
    }

    /// <summary>写操作后的统一收尾：打断引擎休眠 → 刷新悬浮组件 → 重载列表。</summary>
    private async Task AfterChangedAsync()
    {
        _engine.RequestRescan();
        _reloadWidget();
        await RefreshAsync();
    }

    /// <summary>把事项按截止日期分组；无截止排最后，逾期排最前。</summary>
    /// <param name="tasks">事项集合。</param>
    /// <param name="today">今天的日期（分组标题用）。</param>
    /// <param name="todayStatuses">今天已终结的重复事项实例状态（事项 Id → Done/Skipped）。</param>
    private static IReadOnlyList<ItemGroupRow> BuildGroups(
        IReadOnlyList<TaskItem> tasks,
        DateOnly today,
        IReadOnlyDictionary<int, OccurrenceStatus> todayStatuses)
    {
        var groups = new List<ItemGroupRow>();

        var byDate = tasks.GroupBy(static task => task.DueAt is null
            ? (DateOnly?)null
            : DateOnly.FromDateTime(task.DueAt.Value));

        foreach (var group in byDate.OrderBy(static g => g.Key ?? DateOnly.MaxValue))
        {
            var rows = group.Select(task => BuildRow(task, todayStatuses)).ToList();
            groups.Add(new ItemGroupRow(BuildHeader(group.Key, today), rows));
        }

        return groups;
    }

    /// <summary>
    /// 把一条事项投影成展示行。
    /// 重复事项（DAILY/WEEKLY）的模板保持 Pending，行状态改看今天实例是否已终结：
    /// 显示「今日已完成 / 今日已跳过」，并隐藏完成/跳过按钮，避免同一天重复落账。
    /// </summary>
    private static ItemRow BuildRow(TaskItem task, IReadOnlyDictionary<int, OccurrenceStatus> todayStatuses)
    {
        var dueText = task.DueAt is { } due
            ? due.ToString(WindowText.TIME_FORMAT)
            : WindowText.ITEMS_NO_DUE;

        var isRepeating = TaskRecurrenceExpander.NormalizeRule(task.RepeatRule) != TaskRecurrenceExpander.RULE_NONE;

        var statusText = task.Status switch
        {
            TaskItemStatus.Done => WindowText.ITEMS_STATUS_DONE,
            TaskItemStatus.Skipped => WindowText.ITEMS_STATUS_SKIPPED,
            _ => WindowText.ITEMS_STATUS_PENDING,
        };

        var isPending = task.Status == TaskItemStatus.Pending;
        var isDone = task.Status == TaskItemStatus.Done;
        var isSkipped = task.Status == TaskItemStatus.Skipped;

        if (isPending && isRepeating && todayStatuses.TryGetValue(task.Id, out var todayStatus))
        {
            isDone = todayStatus == OccurrenceStatus.Done;
            isSkipped = !isDone;
            isPending = false;
            statusText = isDone ? WindowText.ITEMS_STATUS_TODAY_DONE : WindowText.ITEMS_STATUS_TODAY_SKIPPED;
        }

        return new ItemRow(
            task.Id,
            task.Title,
            dueText,
            GetPriorityLabel(task.Priority),
            task.Priority == TaskPriority.High,
            statusText,
            isPending,
            isDone,
            isSkipped);
    }

    /// <summary>分组标题：今天 / 明天 / 已逾期 / 月日 + 周几 / 无截止。</summary>
    private static string BuildHeader(DateOnly? date, DateOnly today)
    {
        if (date is null)
        {
            return WindowText.ITEMS_GROUP_NO_DUE;
        }

        var day = date.Value;
        if (day == today)
        {
            return string.Format(WindowText.ITEMS_GROUP_TODAY_FORMAT, day);
        }

        if (day == today.AddDays(1))
        {
            return string.Format(WindowText.ITEMS_GROUP_TOMORROW_FORMAT, day);
        }

        if (day < today)
        {
            return string.Format(WindowText.ITEMS_GROUP_OVERDUE_FORMAT, day);
        }

        // 周标签按「周一打头」的索引取（DayOfWeek 的周日 = 0）。
        var weekdayIndex = ((int)day.DayOfWeek + UiConstants.DAYS_FROM_SUNDAY_TO_MONDAY) % UiConstants.DAYS_PER_WEEK;
        return string.Format(WindowText.ITEMS_GROUP_DATE_FORMAT, day, WindowText.WEEKDAY_COLUMN_LABELS[weekdayIndex]);
    }

    /// <summary>优先级显示文案。</summary>
    private static string GetPriorityLabel(TaskPriority priority) => priority switch
    {
        TaskPriority.High => WindowText.TASK_PRIORITY_HIGH,
        TaskPriority.Low => WindowText.TASK_PRIORITY_LOW,
        _ => WindowText.TASK_PRIORITY_NORMAL,
    };

    partial void OnGroupsChanged(IReadOnlyList<ItemGroupRow> value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(PendingCountText));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>一个日期分组：标题 + 行集合。</summary>
    /// <param name="Header">分组标题。</param>
    /// <param name="Items">该组事项。</param>
    public sealed record ItemGroupRow(string Header, IReadOnlyList<ItemRow> Items);

    /// <summary>一行事项的展示数据（视图只做投影，不再计算）。</summary>
    public sealed record ItemRow(
        int Id,
        string Title,
        string DueText,
        string PriorityLabel,
        bool IsHighPriority,
        string StatusText,
        bool IsPending,
        bool IsDone,
        bool IsSkipped);
}
