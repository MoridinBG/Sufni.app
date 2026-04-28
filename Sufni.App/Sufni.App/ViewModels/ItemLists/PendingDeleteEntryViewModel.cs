using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.ItemLists;

/// <summary>
/// One pending-delete entry shown in the stacked undo bar. Owns its
/// own timer, dismiss command, and undo command. The host list view
/// model creates one of these per row deletion and adds it to
/// <see cref="ItemListViewModelBase.PendingDeletes"/>.
/// </summary>
public partial class PendingDeleteEntryViewModel : ObservableObject
{
    private readonly Func<Task> finalize;
    private readonly Action onUndone;
    private readonly Action<PendingDeleteEntryViewModel> remove;
    private CancellationTokenSource? cts;

    public string Name { get; }

    public PendingDeleteEntryViewModel(
        string name,
        Func<Task> finalize,
        Action onUndone,
        Action<PendingDeleteEntryViewModel> remove)
    {
        Name = name;
        this.finalize = finalize;
        this.onUndone = onUndone;
        this.remove = remove;
    }

    public void StartTimer(int delayMs)
    {
        cts = new CancellationTokenSource();
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested) return;
                await CompleteAsync();
            });
        });
    }

    [RelayCommand]
    private Task FinalizeDelete() => CompleteAsync();

    [RelayCommand]
    private void UndoDelete()
    {
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
        cts = null;

        remove(this);
        onUndone();
    }

    private async Task CompleteAsync()
    {
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
        cts = null;

        remove(this);
        await finalize();
    }
}
