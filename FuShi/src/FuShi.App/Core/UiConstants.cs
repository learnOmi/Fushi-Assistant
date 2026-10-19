using System.Windows;

namespace FuShi.Core;

/// <summary>
/// 悬浮组件的界面时序常量。
/// 集中放置，避免窗口逻辑里出现魔法数字（依据设计文档 5.3 交互时序）。
/// </summary>
internal static class UiConstants
{
    /// <summary>鼠标悬停多久后自动展开（毫秒）。设计文档 5.3：悬停 200ms 展开。</summary>
    public const int HOVER_EXPAND_DELAY_MS = 200;

    /// <summary>鼠标移开多久后自动收起（毫秒）。设计文档 5.3：移开 300ms 收起。</summary>
    public const int HOVER_COLLAPSE_DELAY_MS = 300;

    /// <summary>倒计时刷新间隔（毫秒）。UI 侧每秒调一次 ViewModel.Refresh。</summary>
    public const int COUNTDOWN_REFRESH_INTERVAL_MS = 1000;

    /// <summary>
    /// 悬浮组件数据兜底重载间隔（毫秒）。比一秒的倒计时刷新粗得多：用于跨天滚动、
    /// 以及引擎事件之外的安全网；实际数据变化由引擎的 ScheduleChanged 即时推送。
    /// </summary>
    public const int WIDGET_DATA_RELOAD_INTERVAL_MS = 60000;

    /// <summary>托盘图标的悬停提示文本。</summary>
    public const string TRAY_TOOLTIP_TEXT = "浮时 · 课表提醒";

    // ---------- M3 主窗口 / 周网格 / 编辑对话框 ----------

    /// <summary>主窗口初始宽度（设备无关单位）。</summary>
    public const double MAIN_WINDOW_WIDTH = 960.0;

    /// <summary>主窗口初始高度。</summary>
    public const double MAIN_WINDOW_HEIGHT = 620.0;

    /// <summary>主窗口最小宽度。</summary>
    public const double MAIN_WINDOW_MIN_WIDTH = 720.0;

    /// <summary>主窗口最小高度。</summary>
    public const double MAIN_WINDOW_MIN_HEIGHT = 480.0;

    /// <summary>周网格时间列的固定宽度（跨行共享，保证列对齐）。</summary>
    public const double GRID_TIME_COLUMN_WIDTH = 62.0;

    /// <summary>周网格单元格最小高度。</summary>
    public const double GRID_CELL_MIN_HEIGHT = 56.0;

    /// <summary>
    /// 周网格单元格圆角半径。<b>类型必须是 <see cref="CornerRadius"/></b>：
    /// XAML 里 <c>{x:Static}</c> 传的是装箱对象，若声明为 <c>double</c>，
    /// 布局阶段读 <c>Border.CornerRadius</c> 时会抛 InvalidCastException（Double → CornerRadius）。
    /// 因结构体非编译期常量，故用 static readonly。
    /// </summary>
    public static readonly CornerRadius GRID_CELL_CORNER_RADIUS = new(8);

    /// <summary>停用条目的弱化不透明度。</summary>
    public const double GRID_DISABLED_OPACITY = 0.45;

    /// <summary>新建时段时，默认时长（分钟）。</summary>
    public const int EDITOR_DEFAULT_DURATION_MINUTES = 60;

    /// <summary>「添加时段」时把当前时刻向上取整的分钟粒度。</summary>
    public const int EDITOR_TIME_ROUND_MINUTES = 5;

    /// <summary>一周的天数（周网格列数）。</summary>
    public const int DAYS_PER_WEEK = 7;

    /// <summary>一天的小时数（编辑对话框小时下拉上限）。</summary>
    public const int HOURS_PER_DAY = 24;

    /// <summary>编辑对话框分钟下拉的步长（分钟）。</summary>
    public const int MINUTE_STEP = 5;
}
