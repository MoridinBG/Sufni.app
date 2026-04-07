using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;
using Sufni.App.Views.Controls;

namespace Sufni.App.ViewModels;

public partial class TabPageViewModelBase : ViewModelBase
{
    #region Injected services

    protected readonly INavigator navigator;
    protected readonly IDialogService dialogService;

    #endregion Injected services

    #region Constructors

    protected TabPageViewModelBase()
    {
        navigator = null!;
        dialogService = null!;
    }

    protected TabPageViewModelBase(INavigator navigator, IDialogService dialogService)
    {
        this.navigator = navigator;
        this.dialogService = dialogService;
    }

    #endregion Constructors

    #region Navigation helpers

    [RelayCommand]
    protected void OpenPage(ViewModelBase view) => navigator.OpenPage(view);

    [RelayCommand]
    protected void OpenPreviousPage() => navigator.OpenPreviousPage();

    #endregion Navigation helpers

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

    // Used by the EditableTitle control as the optional subtitle. Bike
    // and setup editors leave this null (and the subtitle hides);
    // SessionDetailViewModel sets it to the recording's local-time
    // timestamp after Loaded.
    [ObservableProperty] private DateTime? timestamp;

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
            navigator.CloseTab(this);
            return;
        }

        var result = await dialogService.ShowCloseConfirmationAsync(CanSave());
        switch (result)
        {
            case PromptResult.Yes:
                await Save();
                navigator.CloseTab(this);
                break;
            case PromptResult.No:
                await Reset();
                navigator.CloseTab(this);
                break;
            case PromptResult.Cancel:
                break;
            case PromptResult.Ok:
                await Reset();
                navigator.CloseTab(this);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #endregion Commands
}
