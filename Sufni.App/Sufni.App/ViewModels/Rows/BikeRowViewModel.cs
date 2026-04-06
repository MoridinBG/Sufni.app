using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="BikeSnapshot"/> for use
/// inside a list. Row view models are cheap, non-editable and refresh
/// themselves via <see cref="Update"/> when the underlying snapshot
/// changes. Open/delete commands route through
/// <see cref="IBikeCoordinator"/>.
/// </summary>
public partial class BikeRowViewModel : ObservableObject, IListItemRow
{
    private readonly IBikeCoordinator? bikeCoordinator;

    public Guid Id { get; private set; }

    [ObservableProperty] private string? name;

    // Stub members for IListItemRow. Bikes have no timestamp or
    // sync-completion concept on a row; the controls just hide them.
    public DateTime? Timestamp => null;
    public bool IsComplete => true;

    public BikeRowViewModel()
    {
        bikeCoordinator = null;
    }

    public BikeRowViewModel(BikeSnapshot snapshot, IBikeCoordinator bikeCoordinator)
    {
        this.bikeCoordinator = bikeCoordinator;
        Update(snapshot);
    }

    public void Update(BikeSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
    }

    [RelayCommand]
    private async Task OpenPage()
    {
        if (bikeCoordinator is null) return;
        await bikeCoordinator.OpenEditAsync(Id);
    }

    [RelayCommand]
    private async Task UndoableDelete()
    {
        if (bikeCoordinator is null) return;
        await bikeCoordinator.DeleteAsync(Id);
    }

    [RelayCommand]
    private void FakeDelete()
    {
        // Exists so the controls can bind to a delete command on this row.
    }

    // Explicit interface implementations: the source generator emits
    // IAsyncRelayCommand for async [RelayCommand] methods, which C#
    // does not accept as an implicit implementation of the interface's
    // IRelayCommand property.
    IRelayCommand IListItemRow.OpenPageCommand => OpenPageCommand;
    IRelayCommand IListItemRow.UndoableDeleteCommand => UndoableDeleteCommand;
    IRelayCommand IListItemRow.FakeDeleteCommand => FakeDeleteCommand;
}
