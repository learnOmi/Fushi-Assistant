namespace FuShi.Domain;

/// <summary>
/// 与「实体模型」相关的领域默认值。
/// 集中常量：单行设置表主键、默认配色、默认主题与透明度。
/// </summary>
public static class ModelDefaults
{
    /// <summary>AppSetting 是单行设置表，主键恒为该值。</summary>
    public const int SINGLETON_ROW_ID = 1;

    /// <summary>自举阶段的默认名称（首次进入课表页、无生效阶段时创建）。</summary>
    public const string DEFAULT_PHASE_NAME = "默认课表";

    /// <summary>科目默认颜色（强调色），用户未选色时使用。</summary>
    public const string DEFAULT_SUBJECT_COLOR = "#FF8A5B";

    /// <summary>默认主题名（设计基线为暗色）。</summary>
    public const string DEFAULT_THEME = "dark";

    /// <summary>默认不透明度（完全不透明）。</summary>
    public const double DEFAULT_OPACITY = 1.0;

    /// <summary>悬浮窗不透明度下限（再低会看不见，避免用户把自己"调丢"）。</summary>
    public const double MIN_OPACITY = 0.3;

    /// <summary>悬浮窗不透明度上限。</summary>
    public const double MAX_OPACITY = 1.0;

    /// <summary>默认提前提醒分钟数（0 = 准点提醒，与 M2 行为等价）。</summary>
    public const int DEFAULT_LEAD_TIME_MINUTES = 0;
}
