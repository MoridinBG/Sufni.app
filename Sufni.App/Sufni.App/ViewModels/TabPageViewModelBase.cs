using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Services;
using Sufni.App.Views.Controls;

namespace Sufni.App.ViewModels;

public partial class TabPageViewModelBase : ViewModelBase
{
    #region Observable properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private string? name;

    #endregion Observable properties

    #region Virtual methods

    protected virtual void EvaluateDirtiness() { IsDirty = false; }
    protected virtual Task SaveImplementation() { return Task.CompletedTask; }
    protected virtual Task ResetImplementation() { return Task.CompletedTask; }

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

    [RelayCommand]
    private async Task Close()
    {
        var mainWindowViewModel = App.Current?.Services?.GetService<MainWindowViewModel>();
        var dialogService = App.Current?.Services?.GetService<IDialogService>();
        Debug.Assert(mainWindowViewModel != null);
        Debug.Assert(dialogService != null, nameof(dialogService) + " != null");

        if (!IsDirty)
        {
            mainWindowViewModel.CloseTabPage(this);
            return;
        }
        
        var result = await dialogService.ShowCloseConfirmationAsync(CanSave());
        switch (result)
        {
            case PromptResult.Yes:
                await Save();
                mainWindowViewModel.CloseTabPage(this);
                break;
            case PromptResult.No:
                await Reset();
                mainWindowViewModel.CloseTabPage(this);
                break;
            case PromptResult.Cancel:
                break;
            case PromptResult.Ok:
                await Reset();
                mainWindowViewModel.CloseTabPage(this);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    #endregion Commands
}
