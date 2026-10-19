namespace FuShi.Core;

/// <summary>
/// 科目配色板：新建科目时供用户手选的固定 10 色。
/// 用固定调色板而非第三方取色器：依赖更轻、结果更可控，且颜色与设计令牌同源（暖色强调 + 冷色辅助）。
/// </summary>
internal static class SubjectPalette
{
    /// <summary>可选颜色（#RRGGBB）。首色与设计令牌强调色一致。</summary>
    public static readonly string[] Colors =
    {
        "#FF8A5B", // 暖橙（强调）
        "#F2C14E", // 姜黄
        "#7BC96F", // 草绿
        "#3FA7D6", // 湖蓝
        "#6C7AE0", // 蓝紫
        "#B07DE0", // 紫
        "#E07DA8", // 玫红
        "#E05C5C", // 砖红
        "#8C8C93", // 灰
        "#4FB3A6", // 青
    };
}
