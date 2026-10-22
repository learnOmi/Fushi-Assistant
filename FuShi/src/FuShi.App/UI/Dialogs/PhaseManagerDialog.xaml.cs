using System.ComponentModel;
using System.Windows;
using FuShi.Core;
using FuShi.ViewModels;

namespace FuShi.UI.Dialogs;

/// <summary>
/// 阶段管理对话框（M4a）：列表 + 新建 / 重命名 / 设为当前 / 删除。
/// 所有操作即时落库；视图只负责「删除二次确认」与「关闭时回报是否改过」这类偶然行为，
/// 校验与落库都在 <see cref="PhaseManagerViewModel"/>。
/// </summary>
public partial class PhaseManagerDialog : Window
{
    private readonly PhaseManagerViewModel _viewModel;

    /// <summary>构造。</summary>
    /// <param name="viewModel">阶段管理视图模型。</param>
    public PhaseManagerDialog(PhaseManagerViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Closing += OnWindowClosing;
    }

    /// <summary>删除：二次确认后交给视图模型落库（保护规则由其给出具体提示）。</summary>
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPhase is null)
        {
            // 没有选中项时不弹确认框，直接让视图模型给出「请先选择」提示。
            _viewModel.DeleteSelectedCommand.Execute(null);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            WindowText.PHASE_DELETE_CONFIRM_BODY,
            WindowText.PHASE_DELETE_CONFIRM_TITLE,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _viewModel.DeleteSelectedCommand.Execute(null);
    }

    /// <summary>
    /// 关闭：用 <see cref="PhaseManagerViewModel.HasChanges"/> 作为对话框结果，
    /// 让调用方只在真的改过阶段时才重排调度与刷新界面。
    /// </summary>
    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = _viewModel.HasChanges;

    /// <summary>
    /// 点窗口右上角关闭时与「关闭」按钮口径一致：同样把「是否改过」带回给调用方，
    /// 避免用户改完阶段后直接点 X 导致课表与提醒停留在旧阶段。
    /// </summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (DialogResult is null)
        {
            DialogResult = _viewModel.HasChanges;
        }
    }
}
