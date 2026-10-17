using System.Windows;
using System.Windows.Input;
using FuShi.Core;

namespace FuShi.UI;

/// <summary>
/// 悬浮组件窗口：无边框、置顶、不进任务栏、半透明药丸。
/// 左键拖动移动位置（拖完持久化）；轻点 = 展开/收起今日面板。
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly WidgetStateStore _store;
    private bool _expanded;

    public WidgetWindow(WidgetStateStore store)
    {
        InitializeComponent();
        _store = store;
        UpdateDateText();
    }

    private void UpdateDateText()
    {
        var now = DateTime.Now;
        DateText.Text = $"{now:M月d日} 周{"日一二三四五六"[(int)now.DayOfWeek]}";
    }

    // ---------- 拖动 vs 轻点 ----------
    private void OnGrab(object sender, MouseButtonEventArgs e)
    {
        Win32.GetCursorPos(out var before);
        DragMove();
        Win32.GetCursorPos(out var after);

        double moved = Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y);
        if (moved < SystemParameters.MinimumHorizontalDragDistance + SystemParameters.MinimumVerticalDragDistance)
        {
            ToggleExpand();
        }
        else
        {
            SavePosition(); // 拖完记住位置
        }
    }

    private void ToggleExpand()
    {
        _expanded = !_expanded;
        Expanded.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        Bot.Mood = _expanded ? BotMood.Focus : BotMood.Idle;
    }

    public void SavePosition() => _store.Save(Left, Top);
}
