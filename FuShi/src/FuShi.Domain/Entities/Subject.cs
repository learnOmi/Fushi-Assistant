using FuShi.Domain.Enums;

namespace FuShi.Domain.Entities;

/// <summary>
/// 科目：颜色与分类的锚点（设计文档 2.1）。
/// 课表条目与统计都挂在科目上，「同一科目的时段」用同一颜色呈现。
/// </summary>
public class Subject
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>科目名，如「数学」「英语」。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>展示用颜色（#RRGGBB 十六进制）。</summary>
    public string Color { get; set; } = ModelDefaults.DEFAULT_SUBJECT_COLOR;

    /// <summary>分类（学习 / 工作 / 生活）。</summary>
    public SubjectCategory Category { get; set; }
}
