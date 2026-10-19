using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FuShi.Core;
using FuShi.Domain.Entities;
using FuShi.Domain.Enums;
using FuShi.Infrastructure.Scheduling;

namespace FuShi.ViewModels;

/// <summary>
/// 新建科目对话框的视图模型：名称 + 调色板选色 + 分类。
/// 确定才落库，取消即丢；校验集中在 <see cref="Validate"/>。
/// </summary>
public sealed partial class SubjectEditorViewModel : ObservableObject
{
    private readonly ScheduleEditService _editService;

    /// <summary>可选分类项（值 + 中文标签）。</summary>
    public sealed record CategoryOption(SubjectCategory Value, string Label);

    /// <summary>调色板选项：十六进制 + 预生成的画刷（XAML 直接绑定，免转换器）。</summary>
    public sealed record ColorOption(string Hex, Brush Brush);

    /// <summary>科目名。</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>已选颜色项。</summary>
    [ObservableProperty]
    private ColorOption _selectedColorOption;

    /// <summary>已选分类。</summary>
    [ObservableProperty]
    private CategoryOption _selectedCategory;

    /// <summary>校验提示（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>是否有错误提示。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>可选颜色。</summary>
    public IReadOnlyList<ColorOption> ColorOptions { get; }

    /// <summary>可选分类。</summary>
    public IReadOnlyList<CategoryOption> CategoryOptions { get; }

    /// <summary>落库后的科目；取消时为 null。</summary>
    public Subject? SavedSubject { get; private set; }

    /// <summary>构造。</summary>
    /// <param name="editService">写服务。</param>
    public SubjectEditorViewModel(ScheduleEditService editService)
    {
        _editService = editService;
        ColorOptions = SubjectPalette.Colors.Select(CreateColorOption).ToList();
        _selectedColorOption = ColorOptions[0];

        CategoryOptions = new[]
        {
            new CategoryOption(SubjectCategory.Study, WindowText.CATEGORY_LABELS[(int)SubjectCategory.Study]),
            new CategoryOption(SubjectCategory.Work, WindowText.CATEGORY_LABELS[(int)SubjectCategory.Work]),
            new CategoryOption(SubjectCategory.Life, WindowText.CATEGORY_LABELS[(int)SubjectCategory.Life]),
        };
        _selectedCategory = CategoryOptions[0];
    }

    /// <summary>校验表单；通过返回 true 并清空错误。</summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = WindowText.ERROR_SUBJECT_NAME_REQUIRED;
            return false;
        }

        ErrorText = string.Empty;
        return true;
    }

    /// <summary>落库（假定已通过 <see cref="Validate"/>）。UI 线程同步等待一次微小写入。</summary>
    public void Save()
    {
        SavedSubject = _editService
            .CreateSubjectAsync(Name, SelectedColorOption.Hex, SelectedCategory.Value)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>错误文案变化时同步刷新 <see cref="HasError"/>。</summary>
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>十六进制 → 画刷；非法值回退灰色。</summary>
    private static ColorOption CreateColorOption(string hex)
    {
        Brush brush;
        try
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            brush = Brushes.Gray;
        }

        return new ColorOption(hex, brush);
    }
}
