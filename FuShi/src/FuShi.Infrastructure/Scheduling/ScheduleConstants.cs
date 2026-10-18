namespace FuShi.Infrastructure.Scheduling;

/// <summary>
/// 课表服务的常量：缺省科目名等展示兜底，避免业务代码里出现散落的字符串字面量。
/// </summary>
internal static class ScheduleConstants
{
    /// <summary>科目缺失时的兜底名称（外键约束下不应出现，仅作防御）。</summary>
    public const string UNKNOWN_SUBJECT_NAME = "未命名";
}
