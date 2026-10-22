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

    /// <summary>「事项」页签标题（M4c 落地，当前仅占位显示）。</summary>
    public const string TAB_ITEMS = "事项";

    /// <summary>「统计」页签标题。</summary>
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

    /// <summary>设置页：开机自启标签。</summary>
    public const string SETTINGS_AUTOSTART = "开机自启";

    /// <summary>设置页：开机自启选项文案。</summary>
    public const string SETTINGS_AUTOSTART_ENABLED = "登录 Windows 后自动启动浮时";

    /// <summary>设置页：开机自启说明。</summary>
    public const string SETTINGS_AUTOSTART_HINT = "写入当前用户注册表启动项；移动程序目录后会在下次启动时自动纠正路径。";

    /// <summary>设置页：开机自启失败提示格式（参数为失败原因）。</summary>
    public const string SETTINGS_AUTOSTART_ERROR_FORMAT = "开机自启设置失败，已恢复原状态：{0}";

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

    // ---------- 阶段管理（M4a） ----------

    /// <summary>阶段徽标的悬停提示。</summary>
    public const string PHASE_SWITCH_TOOLTIP = "切换或管理阶段";

    /// <summary>阶段下拉里的「管理阶段…」项。</summary>
    public const string PHASE_MENU_MANAGE = "管理阶段…";

    /// <summary>阶段管理对话框标题。</summary>
    public const string PHASE_MANAGER_TITLE = "阶段管理";

    /// <summary>阶段管理对话框的说明文字。</summary>
    public const string PHASE_MANAGER_HINT = "当前阶段决定课表与提醒；切换后立即按新阶段重排。";

    /// <summary>阶段名输入框的占位/提示标签。</summary>
    public const string PHASE_NAME_LABEL = "阶段名";

    /// <summary>阶段列表项：生效标记。</summary>
    public const string PHASE_ACTIVE_BADGE = "当前";

    /// <summary>阶段列表项：起始日格式（{0} = 日期）。</summary>
    public const string PHASE_START_DATE_FORMAT = "{0:yyyy-MM-dd} 起";

    /// <summary>新建按钮。</summary>
    public const string BUTTON_CREATE = "新建";

    /// <summary>重命名按钮。</summary>
    public const string BUTTON_RENAME = "重命名";

    /// <summary>设为当前（切换生效）按钮。</summary>
    public const string BUTTON_ACTIVATE = "设为当前";

    /// <summary>关闭按钮。</summary>
    public const string BUTTON_CLOSE = "关闭";

    /// <summary>校验错误：阶段名为空。</summary>
    public const string ERROR_PHASE_NAME_REQUIRED = "阶段名不能为空。";

    /// <summary>校验错误：未选中阶段。</summary>
    public const string ERROR_PHASE_NOT_SELECTED = "请先选择一个阶段。";

    /// <summary>删除阶段确认弹窗标题。</summary>
    public const string PHASE_DELETE_CONFIRM_TITLE = "删除阶段";

    /// <summary>删除阶段确认弹窗正文。</summary>
    public const string PHASE_DELETE_CONFIRM_BODY = "确定删除这个阶段吗？该阶段的课表会一并删除，已发生的提醒记录不受影响。";

    /// <summary>删除失败：生效中的阶段。</summary>
    public const string PHASE_DELETE_ACTIVE = "当前生效的阶段不能删除，请先切换到其他阶段。";

    /// <summary>删除失败：已有完成记录。</summary>
    public const string PHASE_DELETE_HAS_HISTORY = "该阶段已有完成记录，不能删除。";

    /// <summary>删除失败：阶段已不存在。</summary>
    public const string PHASE_DELETE_NOT_FOUND = "该阶段已不存在，请关闭后重新打开。";

    /// <summary>阶段操作失败的通用提示（详细原因在日志里）。</summary>
    public const string PHASE_OPERATION_FAILED = "操作失败，请查看日志后重试。";

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

    // ---------- 统计页（M4b） ----------

    /// <summary>区间：本周。</summary>
    public const string STATS_RANGE_THIS_WEEK = "本周";

    /// <summary>区间：本月。</summary>
    public const string STATS_RANGE_THIS_MONTH = "本月";

    /// <summary>区间：自定义。</summary>
    public const string STATS_RANGE_CUSTOM = "自定义";

    /// <summary>自定义区间的「应用」按钮。</summary>
    public const string STATS_APPLY_RANGE = "应用";

    /// <summary>自定义区间输入框之间的分隔文案。</summary>
    public const string STATS_FROM_TO_SEPARATOR = "至";

    /// <summary>自定义日期输入格式。</summary>
    public const string STATS_DATE_FORMAT = "yyyy-MM-dd";

    /// <summary>数字卡：完成率。</summary>
    public const string STATS_CARD_RATE = "完成率";

    /// <summary>数字卡：已投入时长。</summary>
    public const string STATS_CARD_ACTUAL = "已投入";

    /// <summary>数字卡：完成次数。</summary>
    public const string STATS_CARD_DONE = "完成次数";

    /// <summary>科目分布分组标题。</summary>
    public const string STATS_TITLE_SUBJECTS = "科目分布";

    /// <summary>最近 7 天分组标题。</summary>
    public const string STATS_TITLE_RECENT = "最近 7 天";

    /// <summary>空态标题。</summary>
    public const string STATS_EMPTY = "还没有完成记录";

    /// <summary>空态说明。</summary>
    public const string STATS_EMPTY_HINT = "完成或跳过一节课程后，这里会自动汇总。";

    /// <summary>完成率显示格式：{0} = 百分比数值。</summary>
    public const string STATS_RATE_FORMAT = "{0:F0}%";

    /// <summary>时长显示格式（小时）：{0} = 小时数。</summary>
    public const string STATS_HOURS_FORMAT = "{0:0.#} h";

    /// <summary>记录总数副标题格式：{0} = 记录条数。</summary>
    public const string STATS_RECORD_COUNT_FORMAT = "共 {0} 次记录";

    /// <summary>计划时长副标题格式：{0} = 小时数。</summary>
    public const string STATS_PLANNED_FORMAT = "计划 {0:0.#} h";

    /// <summary>跳过/错过副标题格式。</summary>
    public const string STATS_SKIP_MISSED_FORMAT = "跳过 {0} · 错过 {1}";

    /// <summary>科目横条明细格式：{0} = 占比百分比，{1} = 计划分钟数。</summary>
    public const string STATS_SUBJECT_DETAIL_FORMAT = "{0:F0}% · {1} 分钟";

    /// <summary>统计区间显示格式：{0} = 起，{1} = 止。</summary>
    public const string STATS_RANGE_FORMAT = "{0:MM-dd} ~ {1:MM-dd}";

    /// <summary>最近 7 天的日期标签格式。</summary>
    public const string STATS_DAY_LABEL_FORMAT = "MM-dd";

    /// <summary>最近 7 天的单日完成率格式。</summary>
    public const string STATS_DAY_PERCENT_FORMAT = "{0:F0}%";

    /// <summary>当天没有记录时的占位文本。</summary>
    public const string STATS_NO_DATA = "—";

    /// <summary>自定义日期格式错误。</summary>
    public const string STATS_INVALID_DATE = "请输入 yyyy-MM-dd 格式的起止日期。";

    /// <summary>自定义区间起止颠倒。</summary>
    public const string STATS_INVALID_RANGE = "起始日期不能晚于结束日期。";

    /// <summary>统计加载失败的提示。</summary>
    public const string STATS_LOAD_FAILED = "统计数据加载失败，请查看日志。";

    // ---------- 事项页与事项编辑（M4c） ----------

    /// <summary>事项页：新增按钮。</summary>
    public const string ITEMS_ADD = "＋ 添加事项";

    /// <summary>事项页：空态提示。</summary>
    public const string ITEMS_EMPTY_HINT = "还没有事项。点「＋ 添加事项」记一条。";

    /// <summary>事项页头：待办计数格式：{0} = 条数。</summary>
    public const string ITEMS_COUNT_FORMAT = "{0} 条待办";

    /// <summary>事项页：加载失败提示。</summary>
    public const string ITEMS_LOAD_FAILED = "事项加载失败，请查看日志。";

    /// <summary>事项分组：今天。</summary>
    public const string ITEMS_GROUP_TODAY_FORMAT = "今天 · {0:MM-dd}";

    /// <summary>事项分组：明天。</summary>
    public const string ITEMS_GROUP_TOMORROW_FORMAT = "明天 · {0:MM-dd}";

    /// <summary>事项分组：已逾期。</summary>
    public const string ITEMS_GROUP_OVERDUE_FORMAT = "已逾期 · {0:MM-dd}";

    /// <summary>事项分组：普通日期格式（月日 + 周几）。</summary>
    public const string ITEMS_GROUP_DATE_FORMAT = "{0:MM-dd} {1}";

    /// <summary>事项分组：无截止。</summary>
    public const string ITEMS_GROUP_NO_DUE = "无截止";

    /// <summary>事项行：无截止占位。</summary>
    public const string ITEMS_NO_DUE = "无截止";

    /// <summary>事项状态：待办。</summary>
    public const string ITEMS_STATUS_PENDING = "待办";

    /// <summary>事项状态：已完成。</summary>
    public const string ITEMS_STATUS_DONE = "已完成";

    /// <summary>事项状态：已跳过。</summary>
    public const string ITEMS_STATUS_SKIPPED = "已跳过";

    /// <summary>事项行操作：完成。</summary>
    public const string ITEMS_ACTION_DONE = "完成";

    /// <summary>事项行操作：跳过。</summary>
    public const string ITEMS_ACTION_SKIP = "跳过";

    /// <summary>事项行操作：编辑。</summary>
    public const string ITEMS_ACTION_EDIT = "编辑";

    /// <summary>事项编辑：新建标题。</summary>
    public const string TASK_EDITOR_NEW_TITLE = "新建事项";

    /// <summary>事项编辑：编辑标题。</summary>
    public const string TASK_EDITOR_EDIT_TITLE = "编辑事项";

    /// <summary>事项编辑：标题字段。</summary>
    public const string FIELD_TASK_TITLE = "标题";

    /// <summary>事项编辑：截止字段。</summary>
    public const string FIELD_TASK_DUE = "截止";

    /// <summary>事项编辑：优先级字段。</summary>
    public const string FIELD_TASK_PRIORITY = "优先级";

    /// <summary>事项编辑：重复字段。</summary>
    public const string FIELD_TASK_REPEAT = "重复";

    /// <summary>事项编辑：截止时刻勾选项（取消勾选则只显示不提醒）。</summary>
    public const string FIELD_TASK_NO_DUE = "有截止时刻";

    /// <summary>优先级：高。</summary>
    public const string TASK_PRIORITY_HIGH = "高";

    /// <summary>优先级：普通。</summary>
    public const string TASK_PRIORITY_NORMAL = "普通";

    /// <summary>优先级：低。</summary>
    public const string TASK_PRIORITY_LOW = "低";

    /// <summary>重复：不重复。</summary>
    public const string TASK_REPEAT_NONE = "不重复";

    /// <summary>重复：每天。</summary>
    public const string TASK_REPEAT_DAILY = "每天";

    /// <summary>重复：每周。</summary>
    public const string TASK_REPEAT_WEEKLY = "每周";

    /// <summary>事项截止日期输入格式。</summary>
    public const string TASK_DATE_FORMAT = "yyyy-MM-dd";

    /// <summary>事项编辑：标题为空。</summary>
    public const string ERROR_TASK_TITLE_REQUIRED = "事项标题不能为空。";

    /// <summary>事项编辑：日期格式错误。</summary>
    public const string ERROR_TASK_DUE_INVALID = "请输入 yyyy-MM-dd 格式的截止日期。";

    /// <summary>事项编辑：事项已被删除。</summary>
    public const string ERROR_TASK_MISSING = "事项已不存在，可能已被删除。";

    /// <summary>删除事项确认框标题。</summary>
    public const string TASK_DELETE_CONFIRM_TITLE = "删除事项";

    /// <summary>删除事项确认框正文。</summary>
    public const string TASK_DELETE_CONFIRM_BODY = "确定删除这条事项吗？已完成的历史记录会保留。";
}
