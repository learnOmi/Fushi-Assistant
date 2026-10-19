namespace FuShi.Core;

/// <summary>
/// 主窗口 / 周网格 / 编辑对话框的文案常量。
/// 与悬浮组件的 <see cref="WidgetText"/> 分文件：此处集中"管理界面"的文案，避免散落在 XAML 与 ViewModel 中。
/// </summary>
internal static class WindowText
{
    /// <summary>主窗口标题。</summary>
    public const string WINDOW_TITLE = "浮时 · 课表";

    /// <summary>「课表」页签标题。</summary>
    public const string TAB_SCHEDULE = "课表";

    /// <summary>「设置」页签标题。</summary>
    public const string TAB_SETTINGS = "设置";

    /// <summary>设置页占位文案（设置项在 M3b 落地）。</summary>
    public const string SETTINGS_PLACEHOLDER = "设置项将在下一阶段提供。";

    /// <summary>「添加时段」按钮文案。</summary>
    public const string ADD_ENTRY = "＋ 添加时段";

    /// <summary>周网格时间列的列头。</summary>
    public const string TIME_COLUMN_HEADER = "时刻";

    /// <summary>周网格「周一…周日」列头，索引 0..6 对应周一打头。</summary>
    public static readonly string[] WEEKDAY_COLUMN_LABELS = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    /// <summary>阶段名显示格式：{0} = 阶段名。</summary>
    public const string PHASE_FORMAT = "阶段：{0}";

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

    /// <summary>停用角标。</summary>
    public const string DISABLED_BADGE = "已停用";

    /// <summary>删除失败提示（科目被引用等）。</summary>
    public const string DELETE_FAILED = "删除失败：该科目仍被时段引用。";
}
