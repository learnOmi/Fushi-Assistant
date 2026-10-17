namespace FuShi.Core;

/// <summary>
/// 悬浮组件的界面时序常量。
/// 集中放置，避免窗口逻辑里出现魔法数字（依据设计文档 5.3 交互时序）。
/// </summary>
internal static class UiConstants
{
    /// <summary>鼠标悬停多久后自动展开（毫秒）。设计文档 5.3：悬停 200ms 展开。</summary>
    public const int HOVER_EXPAND_DELAY_MS = 200;

    /// <summary>鼠标移开多久后自动收起（毫秒）。设计文档 5.3：移开 300ms 收起。</summary>
    public const int HOVER_COLLAPSE_DELAY_MS = 300;
}
