namespace FuShi.Core;

/// <summary>
/// 提醒弹窗（<c>ReminderPopupWindow</c>）的按钮文案常量。
/// 与其它文案类分文件：此处只放"提醒交互"用到的字面量，避免散落在 XAML 与代码里。
/// </summary>
internal static class ReminderText
{
    /// <summary>「开始」按钮文案（进入进行中，到结束时刻自动记完成）。</summary>
    public const string ACTION_START = "开始";

    /// <summary>「稍后」按钮文案：{0} = 延后分钟数（取 <c>TimeDefaults.DEFAULT_SNOOZE_MINUTES</c>）。</summary>
    public const string ACTION_SNOOZE_FORMAT = "稍后 {0} 分钟";

    /// <summary>「跳过」按钮文案（置跳过并记一条完成记录）。</summary>
    public const string ACTION_SKIP = "跳过";
}
