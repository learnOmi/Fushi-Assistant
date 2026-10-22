using System.Windows;
using FuShi.Core;
using FuShi.ViewModels;

namespace FuShi.UI.Dialogs;

/// <summary>
/// 事项编辑对话框（M4c）：确定 → 校验并落库；取消 → 丢弃；删除 → 二次确认后删除。
/// 视图只做"弹确认框 / 设 DialogResult"这类偶然行为，校验与落库都在 <see cref="TaskEditorViewModel"/>。
/// </summary>
public partial class TaskEditorDialog : Window
{
    private readonly TaskEditorViewModel _viewModel;

    /// <summary>构造。</summary>
    /// <param name="viewModel">编辑视图模型。</param>
    public TaskEditorDialog(TaskEditorViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>确定：校验通过才落库并关闭。</summary>
    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Validate())
        {
            return;
        }

        _viewModel.Save();
        if (_viewModel.HasError)
        {
            return; // 更新时事项已被删除等业务失败：留在对话框展示错误。
        }

        DialogResult = true;
    }

    /// <summary>取消：直接关闭，不落库。</summary>
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>删除：二次确认后落库删除（历史完成记录保留）。</summary>
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            WindowText.TASK_DELETE_CONFIRM_BODY,
            WindowText.TASK_DELETE_CONFIRM_TITLE,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _viewModel.Delete();
        DialogResult = true;
    }
}
