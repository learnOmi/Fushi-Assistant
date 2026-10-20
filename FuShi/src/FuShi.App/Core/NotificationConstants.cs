namespace FuShi.Core;

/// <summary>
/// 通知通道的常量：应用标识（Toast 用）、弹窗尺寸与时序。
/// 集中放置，避免通道与弹窗里出现魔法值。
/// </summary>
internal static class NotificationConstants
{
    /// <summary>
    /// 应用用户模型 ID（AUMID）。非打包桌面应用的 Toast 需要一个稳定的应用标识，
    /// 由社区工具包完成注册；此处保留常量用于日志与排查（风险 6）。
    /// </summary>
    public const string AUMID = "FuShi.FloatTime.Desktop";

    /// <summary>自绘弹窗自动消失时长（毫秒）。到点自行关闭，不打扰用户。用于无按钮的「哑壳」弹窗（如错过汇总）。</summary>
    public const int POPUP_AUTO_DISMISS_MILLISECONDS = 12000;

    /// <summary>
    /// 带动作按钮的弹窗「未响应」超时（毫秒）。超时未选动作即记为一次「未响应」。
    /// 比哑壳弹窗长，给用户留出决策时间（设计 5.4）。
    /// </summary>
    public const int POPUP_NO_RESPONSE_MILLISECONDS = 30000;

    /// <summary>自绘弹窗宽度（设备无关单位）。</summary>
    public const double POPUP_WIDTH = 360.0;

    /// <summary>自绘弹窗距屏幕边缘的留白（设备无关单位）。</summary>
    public const double POPUP_MARGIN = 16.0;

    /// <summary>消息通道文案：错过汇总通知的兜底标题（引擎已给出标题时以引擎为准）。</summary>
    public const string DEFAULT_TITLE = "浮时";
}
