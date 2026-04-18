using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.Rows;

public class ListItemRowViewModelBase : ObservableObject
{
    private string? name;
    private DateTime? timestamp;
    private bool isComplete = true;

    protected ListItemRowViewModelBase()
    {
        OpenPageCommand = new AsyncRelayCommand(OpenPageAsync);
        UndoableDeleteCommand = new RelayCommand(UndoableDelete, CanDelete);
    }

    public string? Name
    {
        get => name;
        protected set => SetProperty(ref name, value);
    }

    public DateTime? Timestamp
    {
        get => timestamp;
        protected set => SetProperty(ref timestamp, value);
    }

    public bool IsComplete
    {
        get => isComplete;
        protected set => SetProperty(ref isComplete, value);
    }

    public IRelayCommand OpenPageCommand { get; }

    public IRelayCommand UndoableDeleteCommand { get; }

    protected virtual Task OpenPageAsync() => Task.CompletedTask;

    protected virtual void UndoableDelete() { }

    protected virtual bool CanDelete() => true;

    protected void NotifyDeleteCanExecuteChanged() => UndoableDeleteCommand.NotifyCanExecuteChanged();
}