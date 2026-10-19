using System.Windows;
using FuShi.ViewModels;

namespace FuShi.UI.Dialogs;

/// <summary>
/// 新建科目对话框：确定 → 校验并落库；取消 → 丢弃。
/// 供时段编辑对话框内联调用（取舍 ③：科目入口内联，不另开页面）。
/// </summary>
public partial class SubjectEditorDialog : Window
{
    private readonly SubjectEditorViewModel _viewModel;

    /// <summary>构造。</summary>
    /// <param name="viewModel">科目编辑视图模型。</param>
    public SubjectEditorDialog(SubjectEditorViewModel viewModel)
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
        DialogResult = true;
    }

    /// <summary>取消：直接关闭，不落库。</summary>
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
