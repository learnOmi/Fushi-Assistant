namespace FuShi.Core;

/// <summary>
/// 悬浮组件的展示文案与格式常量。
/// 集中管理，避免 UI 文案散落在 XAML 与 ViewModel 中；M2b 接入真实数据后这些作为空态兜底。
/// </summary>
internal static class WidgetText
{
    /// <summary>日期显示格式（如「10月18日」）。</summary>
    public const string DATE_FORMAT = "M月d日";

    /// <summary>周几的中文标签，索引为 <c>(int)DayOfWeek</c>。</summary>
    public static readonly string[] WEEKDAY_LABELS = { "日", "一", "二", "三", "四", "五", "六" };

    /// <summary>空课表时的科目位文案。</summary>
    public const string EMPTY_SUBJECT = "今天没有课";

    /// <summary>空课表时的「下一节」位文案（设计文档 5.8 引导态）。</summary>
    public const string EMPTY_NEXT = "点击添加第一个时段";

    /// <summary>展开态的空课表说明文案。</summary>
    public const string EMPTY_EXPANDED_BODY = "团子陪你安心自习。课程与提醒管理将在后续版本提供。";
}
