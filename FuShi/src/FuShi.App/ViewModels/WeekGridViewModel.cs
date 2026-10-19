using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain.Scheduling;

namespace FuShi.ViewModels;

/// <summary>
/// 周网格（App 层）：把领域层纯投影 <see cref="WeekGrid"/> 适配成可绑定的一组行。
/// 领域层负责"算网格"，这里只做"转成 Brush/文案"的展示适配，不含业务判断。
/// </summary>
public sealed class WeekGridViewModel
{
    /// <summary>列头（周一…周日），与 <see cref="WeekGridRowViewModel.Cells"/> 顺序一致。</summary>
    public IReadOnlyList<string> DayHeaders { get; } = WindowText.WEEKDAY_COLUMN_LABELS;

    /// <summary>网格行（按起始时刻升序）。</summary>
    public IReadOnlyList<WeekGridRowViewModel> Rows { get; }

    /// <summary>是否空课表。</summary>
    public bool IsEmpty { get; }

    /// <summary>构造。</summary>
    /// <param name="grid">领域层投影结果。</param>
    /// <param name="onCellClicked">点击单元格的回调（编辑/新建入口）。</param>
    public WeekGridViewModel(WeekGrid grid, Action<WeekGridCellViewModel> onCellClicked)
    {
        Rows = grid.Rows.Select(row => new WeekGridRowViewModel(row, onCellClicked)).ToList();
        IsEmpty = grid.IsEmpty;
    }
}

/// <summary>网格行：左侧时刻 + 七天单元格。</summary>
public sealed class WeekGridRowViewModel
{
    /// <summary>该行的时刻文本（HH:mm）。</summary>
    public string TimeText { get; }

    /// <summary>七天单元格（周一…周日）。</summary>
    public IReadOnlyList<WeekGridCellViewModel> Cells { get; }

    /// <summary>构造。</summary>
    /// <param name="row">领域层行。</param>
    /// <param name="onCellClicked">点击回调。</param>
    public WeekGridRowViewModel(WeekGridRow row, Action<WeekGridCellViewModel> onCellClicked)
    {
        TimeText = row.StartTime.ToString(WindowText.TIME_FORMAT);
        Cells = row.Cells.Select(cell => new WeekGridCellViewModel(cell, onCellClicked)).ToList();
    }
}

/// <summary>
/// 网格单元格：承载"该天该时刻"的展示状态与点击命令。
/// 正常 0 或 1 条；同日同刻多条时取第一条展示（M3 不做密集编辑）。
/// </summary>
public sealed class WeekGridCellViewModel
{
    private readonly WeekGridItem? _item;

    /// <summary>所属星期几。</summary>
    public DayOfWeek DayOfWeek { get; }

    /// <summary>该行对应的起始时刻（空格子点击时用它预填）。</summary>
    public TimeOnly StartTime { get; }

    /// <summary>来源条目主键；空单元格为 0。</summary>
    public int EntryId => _item?.EntryId ?? 0;

    /// <summary>是否有条目。</summary>
    public bool HasEntry => _item is not null;

    /// <summary>科目名（空单元格为空串）。</summary>
    public string SubjectName => _item?.SubjectName ?? string.Empty;

    /// <summary>时刻区间文本（HH:mm - HH:mm）。</summary>
    public string TimeRangeText => _item is null
        ? string.Empty
        : string.Format(
            WindowText.TIME_RANGE_FORMAT,
            _item.StartTime.ToString(WindowText.TIME_FORMAT),
            _item.EndTime.ToString(WindowText.TIME_FORMAT));

    /// <summary>科目色刷（空格子透明）。</summary>
    public Brush SubjectBrush { get; }

    /// <summary>是否与当前时刻重合（高亮）。</summary>
    public bool IsCurrent => _item?.IsCurrent ?? false;

    /// <summary>是否跨午夜（角标）。</summary>
    public bool IsCrossMidnight => _item?.IsCrossMidnight ?? false;

    /// <summary>启用/停用决定整体不透明度。</summary>
    public double CellOpacity { get; }

    /// <summary>角标文本（跨午夜 / 已停用），无则空串。</summary>
    public string BadgeText { get; }

    /// <summary>是否有角标。</summary>
    public bool HasBadge => BadgeText.Length > 0;

    /// <summary>点击命令：空格子 → 以该天该时刻预填新建；有格子 → 编辑。</summary>
    public IRelayCommand ClickCommand { get; }

    /// <summary>构造。</summary>
    /// <param name="cell">领域层单元格。</param>
    /// <param name="onCellClicked">点击回调。</param>
    public WeekGridCellViewModel(WeekGridCell cell, Action<WeekGridCellViewModel> onCellClicked)
    {
        DayOfWeek = cell.DayOfWeek;
        StartTime = cell.StartTime;
        _item = cell.Items.Count > 0 ? cell.Items[0] : null;

        SubjectBrush = _item is null ? Brushes.Transparent : ToBrush(_item.SubjectColor);
        CellOpacity = _item is { IsEnabled: false } ? UiConstants.GRID_DISABLED_OPACITY : 1.0;

        var badges = new List<string>(2);
        if (IsCrossMidnight)
        {
            badges.Add(WindowText.CROSS_MIDNIGHT_BADGE);
        }

        if (_item is { IsEnabled: false })
        {
            badges.Add(WindowText.DISABLED_BADGE);
        }

        BadgeText = string.Join(" · ", badges);
        ClickCommand = new RelayCommand(() => onCellClicked(this));
    }

    /// <summary>十六进制颜色 → 画刷；非法值回退到灰色，不抛。</summary>
    private static Brush ToBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }
}
