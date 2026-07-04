using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Shared.Base;

public partial class TabPageViewModelBase : ViewModelBase
{
    #region Injected services

    protected readonly IShellCoordinator shell;
    protected readonly IDialogService dialogService;

    #endregion Injected services

    #region Constructors

    protected TabPageViewModelBase(IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        shell = null!;
        dialogService = null!;
    }

    protected TabPageViewModelBase(
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        this.shell = shell;
        this.dialogService = dialogService;
    }

    #endregion Constructors

    #region Navigation helpers

    [RelayCommand]
    protected void OpenPreviousPage()
    {
        _ = shell.GoBack();
    }

    #endregion Navigation helpers

    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string? Name { get; set; }

    [ObservableProperty] public partial bool IsTabActive { get; set; }

    // Used by the EditableTitle control as the optional subtitle. Bike
    // and setup editors leave this null (and the subtitle hides);
    // SessionDetailViewModel sets it to the recording's local-time
    // timestamp after Loaded.
    [ObservableProperty] public partial DateTime? Timestamp { get; set; }

    #endregion Observable properties

    #region Virtual methods

    protected virtual void EvaluateDirtiness() { IsDirty = false; }
    protected virtual Task SaveImplementation() { return Task.CompletedTask; }
    protected virtual Task ResetImplementation() { return Task.CompletedTask; }
    protected virtual Task ExportImplementation() { return Task.CompletedTask; }
    protected virtual Task DeleteImplementation(bool navigateBack) { return Task.CompletedTask; }
    protected virtual Task CloseImplementation() { return Task.CompletedTask; }
    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }

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

    protected virtual bool CanDelete() => true;

    #endregion Virtual methods

    partial void OnIsTabActiveChanged(bool value)
    {
        if (value)
        {
            OnActivated();
            return;
        }

        OnDeactivated();
    }

    public void SetTabActive(bool value)
    {
        IsTabActive = value;
    }

    public bool CanDeleteItem => DeleteCommand.CanExecute(false);

    public Task PrepareCloseAsync() => CloseImplementation();

    protected void NotifyDeleteCommandStateChanged()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanDeleteItem));
    }

    protected void NotifyEditorCommandStateChanged()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    #region Commands

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        await SaveImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task Reset()
    {
        await ResetImplementation();
        EvaluateDirtiness();
        NotifyEditorCommandStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        await ExportImplementation();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(bool navigateBack)
    {
        await DeleteImplementation(navigateBack);
    }

    [RelayCommand]
    private async Task Close()
    {
        if (!IsDirty)
        {
            await PrepareCloseAsync();
            shell.Close(this);
            return;
        }

        var result = await dialogService.ShowCloseConfirmationAsync(CanSave());
        switch (result)
        {
            case PromptResult.Yes:
                await Save();
                await PrepareCloseAsync();
                shell.Close(this);
                break;
            case PromptResult.No:
                await Reset();
                await PrepareCloseAsync();
                shell.Close(this);
                break;
            case PromptResult.Cancel:
                break;
            case PromptResult.Ok:
                await Reset();
                await PrepareCloseAsync();
                shell.Close(this);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #endregion Commands
}
