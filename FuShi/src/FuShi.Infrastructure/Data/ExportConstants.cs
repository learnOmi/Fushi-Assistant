namespace FuShi.Infrastructure.Data;

/// <summary>
/// CSV 导出的文件名与表头常量（M5a）。
/// 表头与枚举文案集中在此：Infrastructure 层不能引用 App 层的 WindowText，同时避免业务代码散落字符串。
/// </summary>
public static class ExportConstants
{
    /// <summary>课表导出文件名格式（{0} = 时刻）。</summary>
    public const string SCHEDULE_FILE_FORMAT = "schedule-{0:yyyyMMdd_HHmmss}.csv";

    /// <summary>事项目导出文件名格式（{0} = 时刻）。</summary>
    public const string TASKS_FILE_FORMAT = "tasks-{0:yyyyMMdd_HHmmss}.csv";

    /// <summary>同名冲突时追加的序号格式（{0} = 序号）。</summary>
    public const string FILE_CONFLICT_SUFFIX_FORMAT = "-{0}";

    /// <summary>导出的 CSV 扩展名。</summary>
    public const string FILE_EXTENSION = ".csv";

    /// <summary>课表导出表头。</summary>
    public static readonly string[] SCHEDULE_HEADERS = { "周几", "开始", "结束", "科目", "分类", "备注", "启用" };

    /// <summary>事项导出表头（事项实体没有备注字段，故不输出空列）。</summary>
    public static readonly string[] TASK_HEADERS = { "标题", "截止", "优先级", "重复", "状态" };

    /// <summary>周几标签，按「周一打头」的索引（周日 = 0 映射到下标 6）。</summary>
    public static readonly string[] WEEKDAY_LABELS = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    /// <summary>科目分类标签，索引对应 <c>(int)SubjectCategory</c>。</summary>
    public static readonly string[] CATEGORY_LABELS = { "学习", "工作", "生活" };

    /// <summary>优先级：高。</summary>
    public const string PRIORITY_HIGH = "高";

    /// <summary>优先级：普通。</summary>
    public const string PRIORITY_NORMAL = "普通";

    /// <summary>优先级：低。</summary>
    public const string PRIORITY_LOW = "低";

    /// <summary>事项状态：待办。</summary>
    public const string STATUS_PENDING = "待办";

    /// <summary>事项状态：已完成。</summary>
    public const string STATUS_DONE = "已完成";

    /// <summary>事项状态：已跳过。</summary>
    public const string STATUS_SKIPPED = "已跳过";

    /// <summary>重复：不重复。</summary>
    public const string REPEAT_NONE = "不重复";

    /// <summary>重复：每天。</summary>
    public const string REPEAT_DAILY = "每天";

    /// <summary>重复：每周。</summary>
    public const string REPEAT_WEEKLY = "每周";

    /// <summary>是否启用的文本：是。</summary>
    public const string ENABLED_YES = "是";

    /// <summary>是否启用的文本：否。</summary>
    public const string ENABLED_NO = "否";

    /// <summary>没有值时的占位。</summary>
    public const string EMPTY_VALUE = "";
}
