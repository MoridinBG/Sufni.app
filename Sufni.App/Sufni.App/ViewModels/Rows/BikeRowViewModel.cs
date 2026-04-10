using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="BikeSnapshot"/> for use
/// inside a list. Row view models are cheap, non-editable and refresh
/// themselves via <see cref="Update"/> when the underlying snapshot
/// changes. <see cref="OpenPage"/> routes through
/// <see cref="IBikeCoordinator"/>; <see cref="UndoableDelete"/> hands
/// the row back to the owning list view model via the
/// <c>requestDelete</c> callback so the list can run its pending-delete
/// undo window before finalizing.
///
/// Implements <see cref="IDisposable"/> so the owning list pipeline can
/// release the dependency-query subscription via DynamicData's
/// <c>DisposeMany</c> when a bike leaves the store.
/// </summary>
public partial class BikeRowViewModel : ObservableObject, IListItemRow, IDisposable
{
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly Action<BikeRowViewModel>? requestDelete;
    private readonly IBikeDependencyQuery? dependencyQuery;
    private readonly IDisposable? changesSubscription;

    public Guid Id { get; private set; }

    [ObservableProperty] private string? name;

    // Stub members for IListItemRow. Bikes have no timestamp or
    // sync-completion concept on a row; the controls just hide them.
    public DateTime? Timestamp => null;
    public bool IsComplete => true;

    public BikeRowViewModel()
    {
        bikeCoordinator = null;
        requestDelete = null;
        dependencyQuery = null;
        changesSubscription = null;
    }

    public BikeRowViewModel(
        BikeSnapshot snapshot,
        IBikeCoordinator bikeCoordinator,
        Action<BikeRowViewModel> requestDelete,
        IBikeDependencyQuery dependencyQuery)
    {
        this.bikeCoordinator = bikeCoordinator;
        this.requestDelete = requestDelete;
        this.dependencyQuery = dependencyQuery;
        Update(snapshot);

        // Refresh the delete commands' CanExecute whenever the
        // dependency state may have changed. The subscription is
        // released by the owning list's DisposeMany when this row
        // leaves the store.
        changesSubscription = dependencyQuery.Changes.Subscribe(_ =>
        {
            UndoableDeleteCommand.NotifyCanExecuteChanged();
            FakeDeleteCommand.NotifyCanExecuteChanged();
        });
    }

    public void Update(BikeSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
    }

    public void Dispose()
    {
        changesSubscription?.Dispose();
    }

    private bool CanDelete() =>
        dependencyQuery is null || !dependencyQuery.IsBikeInUse(Id);

    [RelayCommand]
    private async Task OpenPage()
    {
        if (bikeCoordinator is null) return;
        await bikeCoordinator.OpenEditAsync(Id);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void UndoableDelete()
    {
        requestDelete?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // Exists so the controls can bind to a delete command on this row.
    }

    IRelayCommand IListItemRow.OpenPageCommand => OpenPageCommand;
    IRelayCommand IListItemRow.UndoableDeleteCommand => UndoableDeleteCommand;
    IRelayCommand IListItemRow.FakeDeleteCommand => FakeDeleteCommand;
}
