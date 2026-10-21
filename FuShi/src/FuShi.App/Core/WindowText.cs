namespace FuShi.Core;

/// <summary>
/// 主窗口 / 周网格 / 编辑对话框的文案常量。
/// 与悬浮组件的 <see cref="WidgetText"/> 分文件：此处集中"管理界面"的文案，避免散落在 XAML 与 ViewModel 中。
/// </summary>
internal static class WindowText
{
    /// <summary>主窗口标题。</summary>
    public const string WINDOW_TITLE = "浮时 · 课表";

    /// <summary>标题栏品牌名。</summary>
    public const string BRAND_NAME = "浮时";

    /// <summary>「课表」页签标题。</summary>
    public const string TAB_SCHEDULE = "课表";

    /// <summary>「事项」页签标题（M4 落地，当前仅占位显示）。</summary>
    public const string TAB_ITEMS = "事项";

    /// <summary>「统计」页签标题（M4 落地，当前仅占位显示）。</summary>
    public const string TAB_STATS = "统计";

    /// <summary>「设置」页签标题。</summary>
    public const string TAB_SETTINGS = "设置";

    // ---------- 设置页（M3b） ----------

    /// <summary>设置页：不透明度标签。</summary>
    public const string SETTINGS_OPACITY = "悬浮窗不透明度";

    /// <summary>不透明度百分比显示格式：{0} = 百分比数值（0~100）。</summary>
    public const string SETTINGS_OPACITY_PERCENT_FORMAT = "{0:F0}%";

    /// <summary>设置页：免打扰分组标题。</summary>
    public const string SETTINGS_QUIET_TITLE = "免打扰";

    /// <summary>设置页：启用免打扰复选框。</summary>
    public const string SETTINGS_QUIET_ENABLED = "启用免打扰（时段内提醒降级为系统通知）";

    /// <summary>设置页：免打扰起始标签。</summary>
    public const string SETTINGS_QUIET_START = "起始";

    /// <summary>设置页：免打扰结束标签。</summary>
    public const string SETTINGS_QUIET_END = "结束";

    /// <summary>设置页：免打扰说明。</summary>
    public const string SETTINGS_QUIET_HINT = "起止时刻相同视为不启用；跨午夜（如 23:00~07:00）自动识别。";

    /// <summary>设置页：提前提醒标签。</summary>
    public const string SETTINGS_LEAD_TITLE = "提前提醒";

    /// <summary>设置页：提前提醒分钟数显示格式：{0} = 分钟数。</summary>
    public const string SETTINGS_LEAD_MINUTES_FORMAT = "{0} 分钟";

    /// <summary>设置页：提前提醒说明。</summary>
    public const string SETTINGS_LEAD_HINT = "0 = 准点提醒；改动即时重排调度。";

    /// <summary>设置页：时区标签。</summary>
    public const string SETTINGS_TIMEZONE = "时区";

    /// <summary>设置页：时区说明。</summary>
    public const string SETTINGS_TIMEZONE_HINT = "改动按 IANA 时区重展开课表（含夏令时归一）。";

    /// <summary>设置页：主题标签。</summary>
    public const string SETTINGS_THEME = "主题";

    /// <summary>设置页：主题说明。切换即预览，保存后下次启动沿用。</summary>
    public const string SETTINGS_THEME_HINT = "切换后立即生效；点「保存设置」后下次启动沿用。";

    /// <summary>主题名：暗色（基线）。</summary>
    public const string SETTINGS_THEME_DARK = "dark";

    /// <summary>主题名：亮色（预留）。</summary>
    public const string SETTINGS_THEME_LIGHT = "light";

    /// <summary>主题显示标签：暗色。</summary>
    public const string SETTINGS_THEME_DARK_LABEL = "暗色";

    /// <summary>主题显示标签：亮色。</summary>
    public const string SETTINGS_THEME_LIGHT_LABEL = "亮色";

    /// <summary>设置页：保存按钮。</summary>
    public const string SETTINGS_SAVE = "保存设置";

    /// <summary>设置页：保存成功提示。</summary>
    public const string SETTINGS_SAVED = "设置已保存";

    /// <summary>时刻分隔符（时:分）。</summary>
    public const string TIME_SEPARATOR = ":";

    /// <summary>「添加时段」按钮文案。</summary>
    public const string ADD_ENTRY = "＋ 添加时段";

    /// <summary>周网格时间列的列头。</summary>
    public const string TIME_COLUMN_HEADER = "时刻";

    /// <summary>周网格「周一…周日」列头，索引 0..6 对应周一打头。</summary>
    public static readonly string[] WEEKDAY_COLUMN_LABELS = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    /// <summary>阶段名显示格式：{0} = 阶段名。胶囊里已有「当前阶段」的语义，不再重复「阶段：」前缀。</summary>
    public const string PHASE_FORMAT = "{0}";

    /// <summary>空课表（无任何模板）时的提示。</summary>
    public const string EMPTY_GRID_HINT = "还没有任何时段。点「＋ 添加时段」开始排课。";

    /// <summary>新建时段的对话框标题。</summary>
    public const string EDITOR_NEW_TITLE = "新建时段";

    /// <summary>编辑既有条目的对话框标题。</summary>
    public const string EDITOR_EDIT_TITLE = "编辑时段";

    /// <summary>字段：科目。</summary>
    public const string FIELD_SUBJECT = "科目";

    /// <summary>字段：星期。</summary>
    public const string FIELD_DAY = "星期";

    /// <summary>字段：开始时刻。</summary>
    public const string FIELD_START = "开始";

    /// <summary>字段：结束时刻。</summary>
    public const string FIELD_END = "结束";

    /// <summary>字段：备注。</summary>
    public const string FIELD_NOTE = "备注";

    /// <summary>字段：是否启用。</summary>
    public const string FIELD_ENABLED = "启用";

    /// <summary>科目下拉旁的「新建科目」按钮文案。</summary>
    public const string NEW_SUBJECT_BUTTON = "＋ 新建科目";

    /// <summary>新建科目对话框标题。</summary>
    public const string SUBJECT_NEW_TITLE = "新建科目";

    /// <summary>字段：科目名。</summary>
    public const string FIELD_SUBJECT_NAME = "科目名";

    /// <summary>字段：颜色。</summary>
    public const string FIELD_SUBJECT_COLOR = "颜色";

    /// <summary>字段：分类。</summary>
    public const string FIELD_SUBJECT_CATEGORY = "分类";

    /// <summary>确定按钮。</summary>
    public const string BUTTON_OK = "确定";

    /// <summary>取消按钮。</summary>
    public const string BUTTON_CANCEL = "取消";

    /// <summary>删除按钮。</summary>
    public const string BUTTON_DELETE = "删除";

    /// <summary>校验错误：未选科目。</summary>
    public const string ERROR_SUBJECT_REQUIRED = "请选择科目。";

    /// <summary>校验错误：起止时刻相同。</summary>
    public const string ERROR_TIME_EQUAL = "开始与结束时间不能相同。";

    /// <summary>校验错误：科目名为空。</summary>
    public const string ERROR_SUBJECT_NAME_REQUIRED = "科目名不能为空。";

    /// <summary>跨午夜提示（非错误）。</summary>
    public const string HINT_CROSS_MIDNIGHT = "跨午夜：该时段将连续到次日。";

    /// <summary>删除确认弹窗标题。</summary>
    public const string DELETE_CONFIRM_TITLE = "删除时段";

    /// <summary>删除确认弹窗正文。</summary>
    public const string DELETE_CONFIRM_BODY = "确定删除这条时段吗？已发生的提醒记录不会被清除。";

    /// <summary>科目分类标签，索引对应 <c>(int)SubjectCategory</c>（学习 / 工作 / 生活）。</summary>
    public static readonly string[] CATEGORY_LABELS = { "学习", "工作", "生活" };

    /// <summary>时刻显示格式。</summary>
    public const string TIME_FORMAT = "HH:mm";

    /// <summary>时段区间格式：{0} = 开始，{1} = 结束。</summary>
    public const string TIME_RANGE_FORMAT = "{0} - {1}";

    /// <summary>跨午夜角标。</summary>
    public const string CROSS_MIDNIGHT_BADGE = "跨午夜";

    /// <summary>「已顺延」角标（M3c：今天被「稍后」当日顺延过）。</summary>
    public const string SHIFTED_BADGE = "已顺延";

    /// <summary>「已跳过」角标（M3c：今天这一次被用户跳过；比「已顺延」更终态，同时命中时优先）。</summary>
    public const string SKIPPED_BADGE = "已跳过";

    /// <summary>当前正在进行的时段标记（单元格底部，M3.5 视觉稿）。</summary>
    public const string CURRENT_FLAG = "进行中";

    /// <summary>空格子悬停时浮出的新建提示字符。</summary>
    public const string CELL_NEW_HINT = "+";

    /// <summary>空格子的悬停提示语。</summary>
    public const string CELL_NEW_TOOLTIP = "新建时段";

    /// <summary>单元格悬停提示格式：{0} = 星期、{1} = 起始时刻、{2} = 科目名或新建提示。</summary>
    public const string CELL_TOOLTIP_FORMAT = "{0} {1} · {2}";

    /// <summary>停用角标。</summary>
    public const string DISABLED_BADGE = "已停用";

    /// <summary>删除失败提示（科目被引用等）。</summary>
    public const string DELETE_FAILED = "删除失败：该科目仍被时段引用。";
}
