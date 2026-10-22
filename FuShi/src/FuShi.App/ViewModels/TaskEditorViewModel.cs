using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Domain.Recurrence;
using FuShi.Infrastructure.Tasks;

namespace FuShi.ViewModels;

/// <summary>
/// 事项编辑对话框的视图模型（M4c）：标题 / 截止日期与时刻 / 优先级 / 重复档位。
/// <para>
/// 截止用「日期文本 + 时/分下拉」而非日期控件：日期控件在深色主题下难以统一材质，
/// 文本 + 明确格式（yyyy-MM-dd）与统计页自定义区间的交互保持一致，校验也简单。
/// </para>
/// <para>
/// 「无截止」勾选后只保存标题与优先级，事项只显示不提醒（不展开账本、不触发通知）。
/// </para>
/// </summary>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    private readonly TaskService _taskService;

    /// <summary>事项主键；0 表示新建。</summary>
    public int TaskId { get; private set; }

    /// <summary>是否编辑既有事项（决定标题与"删除"按钮可见性）。</summary>
    public bool IsExisting => TaskId != 0;

    /// <summary>对话框标题。</summary>
    public string DialogTitle => IsExisting ? WindowText.TASK_EDITOR_EDIT_TITLE : WindowText.TASK_EDITOR_NEW_TITLE;

    /// <summary>事项标题。</summary>
    [ObservableProperty]
    private string _itemTitle = string.Empty;

    /// <summary>是否有截止时刻。</summary>
    [ObservableProperty]
    private bool _hasDue = true;

    /// <summary>截止日期文本（yyyy-MM-dd）。</summary>
    [ObservableProperty]
    private string _dueDateText = string.Empty;

    /// <summary>可选小时（0~23）。</summary>
    public IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, UiConstants.HOURS_PER_DAY).ToList();

    /// <summary>可选分钟（按步长）。</summary>
    public IReadOnlyList<int> MinuteOptions { get; } = Enumerable
        .Range(0, 60 / UiConstants.MINUTE_STEP)
        .Select(index => index * UiConstants.MINUTE_STEP)
        .ToList();

    /// <summary>截止小时。</summary>
    [ObservableProperty]
    private int _dueHour;

    /// <summary>截止分钟。</summary>
    [ObservableProperty]
    private int _dueMinute;

    /// <summary>优先级选项（高在前，符合录入直觉）。</summary>
    public IReadOnlyList<PriorityOption> PriorityOptions { get; } = new PriorityOption[]
    {
        new(TaskPriority.High, WindowText.TASK_PRIORITY_HIGH),
        new(TaskPriority.Normal, WindowText.TASK_PRIORITY_NORMAL),
        new(TaskPriority.Low, WindowText.TASK_PRIORITY_LOW),
    };

    /// <summary>已选优先级。</summary>
    [ObservableProperty]
    private PriorityOption _selectedPriority;

    /// <summary>重复选项（受控三档，M4 不解析完整 RRULE）。</summary>
    public IReadOnlyList<RepeatOption> RepeatOptions { get; } = new RepeatOption[]
    {
        new(TaskRecurrenceExpander.RULE_NONE, WindowText.TASK_REPEAT_NONE),
        new(TaskRecurrenceExpander.RULE_DAILY, WindowText.TASK_REPEAT_DAILY),
        new(TaskRecurrenceExpander.RULE_WEEKLY, WindowText.TASK_REPEAT_WEEKLY),
    };

    /// <summary>已选重复档位。</summary>
    [ObservableProperty]
    private RepeatOption _selectedRepeat;

    /// <summary>校验错误（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否有错误。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>落库后的事项；取消时为 null。</summary>
    public TaskItem? SavedTask { get; private set; }

    /// <summary>是否已删除（由 <see cref="Delete"/> 置位）。</summary>
    public bool Deleted { get; private set; }

    /// <summary>构造。</summary>
    /// <param name="taskService">事项写服务。</param>
    public TaskEditorViewModel(TaskService taskService)
    {
        _taskService = taskService;
        // 非空默认值：避免 SelectedItem 为 null 时的绑定空档（与 EntryEditor 的星期下拉同一做法）。
        _selectedPriority = PriorityOptions.First(option => option.Value == TaskPriority.Normal);
        _selectedRepeat = RepeatOptions[0];
    }

    /// <summary>以「新建」方式初始化：默认截止为当前时刻 + 默认时长，分钟对齐下拉步长。</summary>
    /// <param name="now">当前时刻。</param>
    public void InitializeNew(DateTime now)
    {
        TaskId = 0;
        var due = now.AddMinutes(UiConstants.EDITOR_DEFAULT_DURATION_MINUTES);
        DueDateText = due.ToString(WindowText.TASK_DATE_FORMAT, CultureInfo.InvariantCulture);
        DueHour = due.Hour;
        DueMinute = RoundToStep(due.Minute);
        HasDue = true;
        SelectedPriority = PriorityOptions.First(option => option.Value == TaskPriority.Normal);
        SelectedRepeat = RepeatOptions[0];
    }

    /// <summary>以「编辑」方式初始化：回填既有事项字段。</summary>
    /// <param name="task">既有事项。</param>
    /// <param name="now">当前时刻（无截止时回填今天的日期，方便重新勾选）。</param>
    public void InitializeExisting(TaskItem task, DateTime now)
    {
        TaskId = task.Id;
        ItemTitle = task.Title;
        HasDue = task.DueAt is not null;

        var due = task.DueAt ?? now;
        DueDateText = due.ToString(WindowText.TASK_DATE_FORMAT, CultureInfo.InvariantCulture);
        DueHour = due.Hour;
        DueMinute = RoundToStep(due.Minute);

        SelectedPriority = PriorityOptions.FirstOrDefault(option => option.Value == task.Priority)
                           ?? PriorityOptions.First(option => option.Value == TaskPriority.Normal);

        var rule = TaskRecurrenceExpander.NormalizeRule(task.RepeatRule);
        SelectedRepeat = RepeatOptions.First(option => option.Value == rule);

        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(IsExisting));
    }

    /// <summary>校验表单；通过返回 true。</summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ItemTitle))
        {
            ErrorText = WindowText.ERROR_TASK_TITLE_REQUIRED;
            return false;
        }

        if (HasDue && !DateTime.TryParseExact(
                DueDateText.Trim(),
                WindowText.TASK_DATE_FORMAT,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            ErrorText = WindowText.ERROR_TASK_DUE_INVALID;
            return false;
        }

        ErrorText = string.Empty;
        return true;
    }

    /// <summary>落库（假定已通过 <see cref="Validate"/>）。UI 线程同步等待一次微小写入。</summary>
    public void Save()
    {
        DateTime? dueAt = null;
        if (HasDue)
        {
            var date = DateTime.ParseExact(
                DueDateText.Trim(),
                WindowText.TASK_DATE_FORMAT,
                CultureInfo.InvariantCulture);
            dueAt = date.Date.AddHours(DueHour).AddMinutes(DueMinute);
        }

        var task = new TaskItem
        {
            Id = TaskId,
            Title = ItemTitle.Trim(),
            DueAt = dueAt,
            Priority = SelectedPriority.Value,
            RepeatRule = SelectedRepeat.Value,
        };

        if (IsExisting)
        {
            var updated = _taskService.UpdateAsync(task, DateTime.Now).GetAwaiter().GetResult();
            if (!updated)
            {
                ErrorText = WindowText.ERROR_TASK_MISSING;
                return;
            }

            SavedTask = task;
            return;
        }

        SavedTask = _taskService.CreateAsync(task, DateTime.Now).GetAwaiter().GetResult();
    }

    /// <summary>删除该事项（历史完成记录保留；未来实例由服务清掉）。</summary>
    public void Delete()
    {
        if (TaskId == 0)
        {
            return;
        }

        _taskService.DeleteAsync(TaskId).GetAwaiter().GetResult();
        Deleted = true;
    }

    /// <summary>把分钟向下取整到下拉步长（保证落在 <see cref="MinuteOptions"/> 内）。</summary>
    private static int RoundToStep(int minute) => minute / UiConstants.MINUTE_STEP * UiConstants.MINUTE_STEP;

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>优先级下拉项（值 + 中文标签）。</summary>
    /// <param name="Value">优先级枚举值。</param>
    /// <param name="Label">显示文案。</param>
    public sealed record PriorityOption(TaskPriority Value, string Label);

    /// <summary>重复档位下拉项（受控文本 + 中文标签）。</summary>
    /// <param name="Value">受控文本（NONE/DAILY/WEEKLY）。</param>
    /// <param name="Label">显示文案。</param>
    public sealed record RepeatOption(string Value, string Label);
}
