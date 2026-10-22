using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuShi.Core;
using FuShi.Domain.Enums;
using FuShi.Infrastructure.Scheduling;
using Serilog;

namespace FuShi.ViewModels;

/// <summary>
/// 阶段管理对话框的视图模型（M4a）：列出全部阶段，提供新建 / 重命名 / 切换生效 / 删除。
/// <para>
/// 所有操作<b>即时落库</b>（没有"确定才保存"的草稿态）——阶段管理是一次性动作，
/// 引入草稿反而要处理"删了又被取消"的歧义。对话框只负责在关闭时告诉调用方「有没有改过」
/// （<see cref="HasChanges"/>），让主窗口决定是否重排调度。
/// </para>
/// </summary>
public sealed partial class PhaseManagerViewModel : ObservableObject
{
    private readonly PhaseService _phaseService;
    private readonly ILogger _logger;

    /// <summary>列表行：阶段主键、名称、是否生效、起始日文案。</summary>
    public sealed record PhaseRow(int Id, string Name, bool IsActive, string StartDateText);

    /// <summary>阶段列表。</summary>
    public ObservableCollection<PhaseRow> Phases { get; } = new();

    /// <summary>当前选中的阶段。</summary>
    [ObservableProperty]
    private PhaseRow? _selectedPhase;

    /// <summary>输入框中的阶段名。</summary>
    [ObservableProperty]
    private string _nameInput = string.Empty;

    /// <summary>校验或业务错误提示（空串表示无错误）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>本次会话是否发生过落库改动；关闭时交给主窗口决定是否重排。</summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>是否有错误提示。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>构造并载入列表。</summary>
    /// <param name="phaseService">阶段管理服务。</param>
    /// <param name="logger">Serilog 日志器。</param>
    public PhaseManagerViewModel(PhaseService phaseService, ILogger logger)
    {
        _phaseService = phaseService;
        _logger = logger;
        Reload();
    }

    /// <summary>新建阶段（不自动生效）。</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!ValidateName())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var created = await _phaseService.CreateAsync(NameInput);
            HasChanges = true;
            NameInput = string.Empty;
            Reload();
            SelectedPhase = Phases.FirstOrDefault(row => row.Id == created.Id);
        });
    }

    /// <summary>重命名选中阶段。</summary>
    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!ValidateName())
        {
            return;
        }

        var target = SelectedPhase;
        if (target is null)
        {
            ErrorText = WindowText.ERROR_PHASE_NOT_SELECTED;
            return;
        }

        await RunAsync(async () =>
        {
            if (await _phaseService.RenameAsync(target.Id, NameInput))
            {
                HasChanges = true;
                Reload();
                SelectedPhase = Phases.FirstOrDefault(row => row.Id == target.Id);
            }
            else
            {
                ErrorText = WindowText.PHASE_DELETE_NOT_FOUND;
            }
        });
    }

    /// <summary>把选中阶段设为当前生效（唯一生效）。</summary>
    [RelayCommand]
    private async Task ActivateAsync()
    {
        var target = SelectedPhase;
        if (target is null)
        {
            ErrorText = WindowText.ERROR_PHASE_NOT_SELECTED;
            return;
        }

        await RunAsync(async () =>
        {
            if (await _phaseService.ActivateAsync(target.Id))
            {
                HasChanges = true;
                Reload();
                SelectedPhase = Phases.FirstOrDefault(row => row.Id == target.Id);
            }
            else
            {
                ErrorText = WindowText.PHASE_DELETE_NOT_FOUND;
            }
        });
    }

    /// <summary>删除选中阶段；三条保护的提示文案与领域结果一一对应。</summary>
    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var target = SelectedPhase;
        if (target is null)
        {
            ErrorText = WindowText.ERROR_PHASE_NOT_SELECTED;
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await _phaseService.DeleteAsync(target.Id);
            switch (outcome)
            {
                case PhaseDeleteOutcome.Deleted:
                    HasChanges = true;
                    Reload();
                    SelectedPhase = null;
                    break;
                case PhaseDeleteOutcome.Active:
                    ErrorText = WindowText.PHASE_DELETE_ACTIVE;
                    break;
                case PhaseDeleteOutcome.HasHistory:
                    ErrorText = WindowText.PHASE_DELETE_HAS_HISTORY;
                    break;
                case PhaseDeleteOutcome.NotFound:
                    ErrorText = WindowText.PHASE_DELETE_NOT_FOUND;
                    break;
            }
        });
    }

    /// <summary>载入选中的阶段名到输入框，便于直接改名。</summary>
    partial void OnSelectedPhaseChanged(PhaseRow? value)
    {
        if (value is not null)
        {
            NameInput = value.Name;
        }
    }

    /// <summary>错误文案变化时同步刷新 <see cref="HasError"/>。</summary>
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>重载列表（同步等待一次读库；内部 ConfigureAwait(false)，UI 线程不死锁）。</summary>
    private void Reload()
    {
        var rows = _phaseService.GetPhasesAsync().GetAwaiter().GetResult()
            .Select(static p => new PhaseRow(
                p.Id,
                p.Name,
                p.IsActive,
                string.Format(WindowText.PHASE_START_DATE_FORMAT, p.StartDate)))
            .ToList();

        Phases.Clear();
        foreach (var row in rows)
        {
            Phases.Add(row);
        }
    }

    /// <summary>校验名称非空；通过时清空错误。</summary>
    private bool ValidateName()
    {
        if (string.IsNullOrWhiteSpace(NameInput))
        {
            ErrorText = WindowText.ERROR_PHASE_NAME_REQUIRED;
            return false;
        }

        ErrorText = string.Empty;
        return true;
    }

    /// <summary>统一执行一次写操作：异常记日志并转为界面提示，不让 async 命令把异常抛成崩溃。</summary>
    /// <param name="action">写操作。</param>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            ErrorText = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "阶段管理操作失败");
            ErrorText = WindowText.PHASE_OPERATION_FAILED;
        }
    }
}
