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

    /// <summary>
    /// 已跳过条目「内容层」的弱化不透明度（M3.5 视觉稿 §4.2：只弱化内容，不弱化到不可读）。
    /// 类型为 <see cref="double"/>，XAML 侧用 <c>{x:Static}</c> 直接取。
    /// </summary>
    public const double GRID_SKIPPED_CONTENT_OPACITY = 0.62;

    /// <summary>新建时段时，默认时长（分钟）。</summary>
    public const int EDITOR_DEFAULT_DURATION_MINUTES = 60;

    /// <summary>「添加时段」时把当前时刻向上取整的分钟粒度（与 <see cref="MINUTE_STEP"/> 保持一致，保证取整结果落在下拉档位内）。</summary>
    public const int EDITOR_TIME_ROUND_MINUTES = 1;

    /// <summary>一周的天数（周网格列数）。</summary>
    public const int DAYS_PER_WEEK = 7;

    /// <summary>一天的小时数（编辑对话框小时下拉上限）。</summary>
    public const int HOURS_PER_DAY = 24;

    /// <summary>编辑对话框与设置页分钟下拉的步长（分钟）。1 = 每分钟一档（0–59）。</summary>
    public const int MINUTE_STEP = 1;

    // ---------- M3b 设置页 ----------

    /// <summary>提前提醒分钟数的可选档位（下拉预设）。</summary>
    public static readonly int[] LEAD_TIME_PRESET_MINUTES = { 0, 5, 10, 15, 20, 30 };

    /// <summary>免打扰「未设置」时，勾选启用后的默认起始小时（22:00）。</summary>
    public const int QUIET_DEFAULT_START_HOUR = 22;

    /// <summary>免打扰「未设置」时，勾选启用后的默认结束小时（07:00，跨午夜）。</summary>
    public const int QUIET_DEFAULT_END_HOUR = 7;

    /// <summary>免打扰默认结束分钟。</summary>
    public const int QUIET_DEFAULT_END_MINUTE = 0;

    /// <summary>
    /// 「设置已保存」提示的停留时长（毫秒）。到时自动淡出，避免提示永久挂在页面上。
    /// </summary>
    public const int SAVED_HINT_DURATION_MILLISECONDS = 3000;

    // ---------- M4b 统计页 ----------

    /// <summary>最近 7 天柱状条的高度（设备无关单位）。</summary>
    public const double STATS_DAY_CHART_HEIGHT = 64.0;

    /// <summary>科目分布横条的厚度。</summary>
    public const double STATS_SUBJECT_BAR_HEIGHT = 6.0;

    /// <summary>周网格/统计共用的「一周七天」偏移：周日 = 0 映射到周一打头的下标。</summary>
    public const int DAYS_FROM_SUNDAY_TO_MONDAY = 6;

    /// <summary>百分比满值（横条/柱状条按 0~100 归一）。</summary>
    public const double STATS_PERCENT_FULL = 100.0;
}
