using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Infrastructure.Scheduling;

namespace FuShi.ViewModels;

/// <summary>
/// 时段编辑对话框的视图模型：科目 / 星期 / 起止时刻 / 备注 / 启用。
/// 起止用「小时 + 分钟」两个下拉而非自由文本，天然规避解析错误。
/// <para>
/// 校验规则：科目必选；起止不能相同。跨午夜（结束早于开始）<b>不是错误</b>，只给提示。
/// 确定才落库（<see cref="Save"/>），删除走 <see cref="Delete"/>（已发生的账本不清）。
/// </para>
/// </summary>
public sealed partial class EntryEditorViewModel : ObservableObject
{
    private readonly ScheduleEditService _editService;
    private readonly Func<SubjectEditorViewModel, bool> _showSubjectEditor;

    /// <summary>星期下拉项（值 + 中文标签）。</summary>
    public sealed record DayOption(DayOfWeek Value, string Label);

    private int _phaseId;

    /// <summary>条目主键；0 表示新建。</summary>
    public int EntryId { get; private set; }

    /// <summary>是否编辑既有条目（决定标题与"删除"按钮可见性）。</summary>
    public bool IsExisting => EntryId != 0;

    /// <summary>对话框标题。</summary>
    public string Title => IsExisting ? WindowText.EDITOR_EDIT_TITLE : WindowText.EDITOR_NEW_TITLE;

    /// <summary>可选科目。</summary>
    public ObservableCollection<Subject> Subjects { get; } = new();

    /// <summary>已选科目。</summary>
    [ObservableProperty]
    private Subject? _selectedSubject;

    /// <summary>可选星期。</summary>
    public IReadOnlyList<DayOption> DayOptions { get; }

    /// <summary>已选星期。</summary>
    [ObservableProperty]
    private DayOption _selectedDay;

    /// <summary>可选小时（0~23）。</summary>
    public IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, UiConstants.HOURS_PER_DAY).ToList();

    /// <summary>可选分钟（0–59，每分钟一档）。</summary>
    public IReadOnlyList<int> MinuteOptions { get; } = Enumerable
        .Range(0, 60 / UiConstants.MINUTE_STEP)
        .Select(i => i * UiConstants.MINUTE_STEP)
        .ToList();

    /// <summary>开始小时。</summary>
    [ObservableProperty]
    private int _startHour;

    /// <summary>开始分钟。</summary>
    [ObservableProperty]
    private int _startMinute;

    /// <summary>结束小时。</summary>
    [ObservableProperty]
    private int _endHour;

    /// <summary>结束分钟。</summary>
    [ObservableProperty]
    private int _endMinute;

    /// <summary>备注。</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>是否启用。</summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>校验错误（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否有错误。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>是否显示跨午夜提示。</summary>
    public bool HasCrossMidnightHint =>
        new TimeOnly(EndHour, EndMinute) < new TimeOnly(StartHour, StartMinute);

    /// <summary>落库后的条目；取消时为 null。</summary>
    public ScheduleEntry? SavedEntry { get; private set; }

    /// <summary>是否已删除（由 <see cref="Delete"/> 置位）。</summary>
    public bool Deleted { get; private set; }

    /// <summary>构造。</summary>
    /// <param name="editService">写服务。</param>
    /// <param name="showSubjectEditor">弹出"新建科目"对话框的委托（返回是否确定）。</param>
    public EntryEditorViewModel(
        ScheduleEditService editService,
        Func<SubjectEditorViewModel, bool> showSubjectEditor)
    {
        _editService = editService;
        _showSubjectEditor = showSubjectEditor;
        DayOptions = BuildDayOptions();
        _selectedDay = DayOptions[0];
        LoadSubjects();
    }

    /// <summary>以「新建」方式初始化：预填星期与开始时刻，结束 = 开始 + 默认时长。</summary>
    /// <param name="phaseId">生效阶段主键。</param>
    /// <param name="day">预填星期。</param>
    /// <param name="start">预填开始时刻。</param>
    public void InitializeNew(int phaseId, DayOfWeek day, TimeOnly start)
    {
        _phaseId = phaseId;
        EntryId = 0;
        SelectedDay = DayOptions.First(option => option.Value == day);
        StartHour = start.Hour;
        StartMinute = RoundToStep(start.Minute);

        var end = start.AddMinutes(UiConstants.EDITOR_DEFAULT_DURATION_MINUTES);
        EndHour = end.Hour;
        EndMinute = RoundToStep(end.Minute);
    }

    /// <summary>以「编辑」方式初始化：回填既有条目字段。</summary>
    /// <param name="phaseId">生效阶段主键。</param>
    /// <param name="entry">既有条目。</param>
    public void InitializeExisting(int phaseId, ScheduleEntry entry)
    {
        _phaseId = phaseId;
        EntryId = entry.Id;
        SelectedDay = DayOptions.First(option => option.Value == entry.DayOfWeek);
        StartHour = entry.StartTime.Hour;
        StartMinute = RoundToStep(entry.StartTime.Minute);
        EndHour = entry.EndTime.Hour;
        EndMinute = RoundToStep(entry.EndTime.Minute);
        Note = entry.Note ?? string.Empty;
        Enabled = entry.Enabled;
        SelectedSubject = Subjects.FirstOrDefault(subject => subject.Id == entry.SubjectId) ?? SelectedSubject;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsExisting));
    }

    /// <summary>弹出「新建科目」对话框；确定则落库并自动选中。</summary>
    [RelayCommand]
    private void NewSubject()
    {
        var subjectVm = new SubjectEditorViewModel(_editService);
        if (!_showSubjectEditor(subjectVm) || subjectVm.SavedSubject is null)
        {
            return;
        }

        Subjects.Add(subjectVm.SavedSubject);
        SelectedSubject = subjectVm.SavedSubject;
    }

    /// <summary>校验表单；通过返回 true。</summary>
    public bool Validate()
    {
        if (SelectedSubject is null)
        {
            ErrorText = WindowText.ERROR_SUBJECT_REQUIRED;
            return false;
        }

        if (StartHour == EndHour && StartMinute == EndMinute)
        {
            ErrorText = WindowText.ERROR_TIME_EQUAL;
            return false;
        }

        ErrorText = string.Empty;
        return true;
    }

    /// <summary>落库（假定已通过 <see cref="Validate"/>）。UI 线程同步等待一次微小写入。</summary>
    public void Save()
    {
        var entry = new ScheduleEntry
        {
            Id = EntryId,
            PhaseId = _phaseId,
            SubjectId = SelectedSubject!.Id,
            DayOfWeek = SelectedDay.Value,
            StartTime = new TimeOnly(StartHour, StartMinute),
            EndTime = new TimeOnly(EndHour, EndMinute),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            Enabled = Enabled,
        };

        SavedEntry = _editService.UpsertEntryAsync(entry).GetAwaiter().GetResult();
    }

    /// <summary>删除该条目（历史 Occurrence 不动；对账由调用方负责）。</summary>
    public void Delete()
    {
        if (EntryId == 0)
        {
            return;
        }

        _editService.DeleteEntryAsync(EntryId).GetAwaiter().GetResult();
        Deleted = true;
    }

    /// <summary>把「开始/结束 + 备注」相关变化同步刷新派生属性（标题、跨午夜提示）。</summary>
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnStartHourChanged(int value) => RaiseDerived();

    partial void OnStartMinuteChanged(int value) => RaiseDerived();

    partial void OnEndHourChanged(int value) => RaiseDerived();

    partial void OnEndMinuteChanged(int value) => RaiseDerived();

    private void RaiseDerived() => OnPropertyChanged(nameof(HasCrossMidnightHint));

    /// <summary>加载全部科目并默认选中第一个（或保持已选）。</summary>
    private void LoadSubjects()
    {
        var subjects = _editService.GetSubjectsAsync().GetAwaiter().GetResult();
        foreach (var subject in subjects)
        {
            Subjects.Add(subject);
        }

        if (SelectedSubject is null && Subjects.Count > 0)
        {
            SelectedSubject = Subjects[0];
        }
    }

    /// <summary>把分钟向下取整到下拉步长（保证落在 <see cref="MinuteOptions"/> 内）。</summary>
    private static int RoundToStep(int minute) => minute / UiConstants.MINUTE_STEP * UiConstants.MINUTE_STEP;

    /// <summary>构造周一打头的星期选项。</summary>
    private static IReadOnlyList<DayOption> BuildDayOptions()
    {
        var options = new List<DayOption>(UiConstants.DAYS_PER_WEEK);
        for (var index = 0; index < UiConstants.DAYS_PER_WEEK; index++)
        {
            // 显示顺序 0..6 → 周一(1)..周日(0)，与周网格列一致。
            var day = (DayOfWeek)((index + 1) % UiConstants.DAYS_PER_WEEK);
            options.Add(new DayOption(day, WindowText.WEEKDAY_COLUMN_LABELS[index]));
        }

        return options;
    }
}
