using Sufni.App.Shell.Coordinators;
using System;
using System.Threading.Tasks;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Shared.Base;
namespace Sufni.App.Bikes.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="BikeSnapshot"/> for use
/// inside a list. Row view models are cheap, non-editable and refresh
/// themselves via <see cref="Update"/> when the underlying snapshot
/// changes. <see cref="OpenPage"/> routes through
/// <see cref="IShellCoordinator"/>; <see cref="UndoableDelete"/> hands
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
    private readonly IShellCoordinator shell;
    private readonly Action<BikeRowViewModel> requestDelete;
    private readonly IBikeDependencyQuery dependencyQuery;
    private readonly IDisposable changesSubscription;

    public Guid Id { get; private set; }

    public BikeRowViewModel(
        BikeSnapshot snapshot,
        IShellCoordinator shell,
        Action<BikeRowViewModel> requestDelete,
        IBikeDependencyQuery dependencyQuery)
    {
        this.shell = shell;
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
        changesSubscription.Dispose();
    }

    protected override Task OpenPageAsync()
    {
        shell.OpenEditor(new EditorKey.Bike(Id));
        return Task.CompletedTask;
    }

    protected override void UndoableDelete()
    {
        requestDelete(this);
    }

    protected override bool CanDelete() =>
        !dependencyQuery.IsBikeInUse(Id);
}
