using Sufni.App.Shared.Base;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shell.Coordinators;

namespace Sufni.App.Shell.ViewModels.Base;

/// <summary>An undo-bar projection of a shell-owned pending delete.</summary>
public partial class PendingDeleteEntryViewModel(PendingDelete pending, IPendingDeletes owner) : ObservableObject
{
    public string Name => pending.Name;
    public PendingDeleteKey Key => pending.Key;

    [RelayCommand]
    private Task FinalizeDelete() => owner.CommitAsync(Key);

    [RelayCommand]
    private void UndoDelete() => owner.Undo(Key);
}
