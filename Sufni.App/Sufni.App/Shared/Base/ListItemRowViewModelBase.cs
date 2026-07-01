using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.Shared.Base;

public class ListItemRowViewModelBase : ObservableObject
{
    protected ListItemRowViewModelBase()
    {
        OpenPageCommand = new AsyncRelayCommand(OpenPageAsync);
        UndoableDeleteCommand = new RelayCommand(UndoableDelete, CanDelete);
    }

    public string? Name
    {
        get => field;
        protected set => SetProperty(ref field, value);
    }

    public DateTime? Timestamp
    {
        get => field;
        protected set => SetProperty(ref field, value);
    }

    public bool IsComplete
    {
        get => field;
        protected set => SetProperty(ref field, value);
    } = true;

    public IRelayCommand OpenPageCommand { get; }

    public IRelayCommand UndoableDeleteCommand { get; }

    protected virtual Task OpenPageAsync() => Task.CompletedTask;

    protected virtual void UndoableDelete() { }

    protected virtual bool CanDelete() => true;

    protected void NotifyDeleteCanExecuteChanged() => UndoableDeleteCommand.NotifyCanExecuteChanged();
}