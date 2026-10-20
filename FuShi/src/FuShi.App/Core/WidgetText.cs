namespace FuShi.Core;

/// <summary>
/// 悬浮组件的展示文案与格式常量。
/// 集中管理，避免 UI 文案散落在 XAML 与 ViewModel 中；空态文案作为「今天没有课」的兜底。
/// </summary>
internal static class WidgetText
{
    /// <summary>日期显示格式（如「10月18日」）。</summary>
    public const string DATE_FORMAT = "M月d日";

    /// <summary>时刻显示格式（如「14:00」）。</summary>
    public const string CLOCK_FORMAT = "HH:mm";

    /// <summary>周几的中文标签，索引为 <c>(int)DayOfWeek</c>。</summary>
    public static readonly string[] WEEKDAY_LABELS = { "日", "一", "二", "三", "四", "五", "六" };

    /// <summary>空课表时的科目位文案。</summary>
    public const string EMPTY_SUBJECT = "今天没有课";

    /// <summary>空课表时的「下一节」位文案（设计文档 5.8 引导态）。</summary>
    public const string EMPTY_NEXT = "点击添加第一个时段";

    /// <summary>展开态的空课表说明文案。</summary>
    public const string EMPTY_EXPANDED_BODY = "团子陪你安心自习。课程与提醒管理将在后续版本提供。";

    /// <summary>倒计时（当前节剩余）：{0} = 时长。</summary>
    public const string COUNTDOWN_REMAINING_FORMAT = "还剩 {0}";

    /// <summary>倒计时（下一节距开始）：{0} = 时长。</summary>
    public const string COUNTDOWN_UNTIL_START_FORMAT = "距开始 {0}";

    /// <summary>「已跳过」状态文案。</summary>
    public const string STATUS_SKIPPED = "已跳过";

    /// <summary>「已顺延」徽标文案（M3c：该时段被「稍后」当日顺延过）。</summary>
    public const string SHIFTED_BADGE = "已顺延";

    /// <summary>时长格式化（≥1 小时）：{0} = 小时，{1} = 分钟（两位）。</summary>
    public const string DURATION_HOURS_FORMAT = "{0}小时{1:D2}分";

    /// <summary>时长格式化（&lt;1 小时）：{0} = 分钟，{1} = 秒（两位）。</summary>
    public const string DURATION_MINUTES_FORMAT = "{0}分{1:D2}秒";

    /// <summary>「下一节」文案（今天）：{0} = 时刻，{1} = 科目。</summary>
    public const string NEXT_TODAY_FORMAT = "下一节 {0} {1}";

    /// <summary>「下一节」文案（明天）：{0} = 时刻，{1} = 科目。</summary>
    public const string NEXT_TOMORROW_FORMAT = "明天 {0} {1}";

    /// <summary>「下一节」文案（更远的某天）：{0} = 周几标签，{1} = 时刻，{2} = 科目。</summary>
    public const string NEXT_LATER_FORMAT = "周{0} {1} {2}";

    /// <summary>今天已无更多课时的「下一节」位文案。</summary>
    public const string LAST_OF_DAY_TEXT = "今天最后一节";

    /// <summary>展开态正文前缀：{0} = 今天剩余时段的拼接。</summary>
    public const string EXPANDED_BODY_FORMAT = "接下来：{0}";

    /// <summary>展开态正文里单个时段的格式：{0} = 时刻，{1} = 科目。</summary>
    public const string EXPANDED_ITEM_FORMAT = "{0} {1}";

    /// <summary>展开态正文里时段之间的分隔符。</summary>
    public const string EXPANDED_ITEM_SEPARATOR = "  ·  ";
}
