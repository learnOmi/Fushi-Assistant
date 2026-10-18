namespace FuShi.Domain.Enums;

/// <summary>
/// 科目分类（设计文档 2.1 Subject.category）。
/// 用于统计页按「学习 / 工作 / 生活」三档聚合，也是配色分组的锚点。
/// </summary>
public enum SubjectCategory
{
    /// <summary>学习（考研/升学主线）。</summary>
    Study = 0,

    /// <summary>工作（实习/项目/竞赛）。</summary>
    Work = 1,

    /// <summary>生活（作息/运动/杂务）。</summary>
    Life = 2,
}
