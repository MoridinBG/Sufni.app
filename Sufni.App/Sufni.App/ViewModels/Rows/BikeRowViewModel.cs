using System;
using System.Threading.Tasks;
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
public sealed class BikeRowViewModel : ListItemRowViewModelBase, IDisposable
{
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly Action<BikeRowViewModel>? requestDelete;
    private readonly IBikeDependencyQuery? dependencyQuery;
    private readonly IDisposable? changesSubscription;

    public Guid Id { get; private set; }

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
            NotifyDeleteCanExecuteChanged();
        });
    }

    public void Update(BikeSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        Timestamp = null;
        IsComplete = true;
    }

    public void Dispose()
    {
        changesSubscription?.Dispose();
    }

    protected override async Task OpenPageAsync()
    {
        if (bikeCoordinator is null) return;
        await bikeCoordinator.OpenEditAsync(Id);
    }

    protected override void UndoableDelete()
    {
        requestDelete?.Invoke(this);
    }

    protected override bool CanDelete() =>
        dependencyQuery is null || !dependencyQuery.IsBikeInUse(Id);
}
