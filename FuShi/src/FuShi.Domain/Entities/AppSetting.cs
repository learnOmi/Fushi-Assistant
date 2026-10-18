namespace FuShi.Domain.Entities;

/// <summary>
/// 应用设置：单行配置表（设计文档 2.1），取代 M1 的 widget.json。
/// 主键恒为 <see cref="ModelDefaults.SINGLETON_ROW_ID"/>，全表只有一行。
/// </summary>
public class AppSetting
{
    /// <summary>主键（恒为 1，单行表）。</summary>
    public int Id { get; set; } = ModelDefaults.SINGLETON_ROW_ID;

    /// <summary>是否开机自启。</summary>
    public bool Autostart { get; set; }

    /// <summary>悬浮窗上次位置 X（设备无关单位）。</summary>
    public double WidgetX { get; set; }

    /// <summary>悬浮窗上次位置 Y（设备无关单位）。</summary>
    public double WidgetY { get; set; }

    /// <summary>是否存在已保存的位置（false 时按默认右上角摆放）。</summary>
    public bool HasWidgetPosition { get; set; }

    /// <summary>悬浮窗不透明度（0~1）。</summary>
    public double Opacity { get; set; } = ModelDefaults.DEFAULT_OPACITY;

    /// <summary>当前时区（IANA，如 Europe/London）。</summary>
    public string TimezoneId { get; set; } = TimeDefaults.DEFAULT_TIMEZONE_ID;

    /// <summary>免打扰起始时刻；null 表示不启用免打扰。</summary>
    public TimeOnly? QuietStart { get; set; }

    /// <summary>免打扰结束时刻。</summary>
    public TimeOnly? QuietEnd { get; set; }

    /// <summary>默认提前提醒分钟数（无规则覆盖时使用）。</summary>
    public int LeadTimeMinutes { get; set; }

    /// <summary>主题名。</summary>
    public string Theme { get; set; } = ModelDefaults.DEFAULT_THEME;
}
