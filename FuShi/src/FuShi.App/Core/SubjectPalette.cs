namespace FuShi.Core;

/// <summary>
/// 科目配色板：新建科目时供用户手选的固定 10 色。
/// 用固定调色板而非第三方取色器：依赖更轻、结果更可控，且颜色与设计令牌同源（冷色为主、暖色点缀）。
/// <para>
/// M3.5 重排：首色改为湖蓝 <c>#3FA7D6</c>（与默认科目色一致），并移除橙红 <c>#FF8A5B</c>——
/// 它和「进行中」的强调色 / 提醒色太近，一眼分不清是科目色还是状态色。
/// </para>
/// </summary>
internal static class SubjectPalette
{
    /// <summary>可选颜色（#RRGGBB），首色即 <see cref="FuShi.Domain.ModelDefaults.DEFAULT_SUBJECT_COLOR"/>。</summary>
    public static readonly string[] Colors =
    {
        "#3FA7D6", // 湖蓝（默认）
        "#4A6FA5", // 深海蓝
        "#6C7AE0", // 蓝紫
        "#B07DE0", // 紫
        "#E07DA8", // 玫红
        "#E05C5C", // 砖红
        "#F2C14E", // 姜黄
        "#7BC96F", // 草绿
        "#4FB3A6", // 青
        "#8C8C93", // 灰
    };
}
