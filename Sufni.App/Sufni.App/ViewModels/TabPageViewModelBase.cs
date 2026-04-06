using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;
using Sufni.App.Views.Controls;

namespace Sufni.App.ViewModels;

public partial class TabPageViewModelBase : ViewModelBase
{
    #region Static services

    public static IDialogService DialogService { get; set; } = null!;

    #endregion Static services

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private string? name;

    #endregion Observable properties

    #region Virtual methods

    protected virtual void EvaluateDirtiness() { IsDirty = false; }
    protected virtual Task SaveImplementation() { return Task.CompletedTask; }
    protected virtual Task ResetImplementation() { return Task.CompletedTask; }
    protected virtual Task ExportImplementation() { return Task.CompletedTask; }

    protected virtual bool CanSave()
    {
        EvaluateDirtiness();
        return IsDirty;
    }

    protected virtual bool CanReset()
    {
        EvaluateDirtiness();
        return IsDirty;
    }

    protected virtual bool CanExport()
    {
        EvaluateDirtiness();
        return !IsDirty;
    }

    #endregion Virtual methods

    #region Commands

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        await SaveImplementation();
        IsDirty = false;
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task Reset()
    {
        await ResetImplementation();
        IsDirty = false;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        await ExportImplementation();
    }

    [RelayCommand]
    private async Task Close()
    {
        if (!IsDirty)
        {
            Navigator.CloseTab(this);
            return;
        }

        var result = await DialogService.ShowCloseConfirmationAsync(CanSave());
        switch (result)
        {
            case PromptResult.Yes:
                await Save();
                Navigator.CloseTab(this);
                break;
            case PromptResult.No:
                await Reset();
                Navigator.CloseTab(this);
                break;
            case PromptResult.Cancel:
                break;
            case PromptResult.Ok:
                await Reset();
                Navigator.CloseTab(this);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #endregion Commands
}
