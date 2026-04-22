using System;
using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="SetupSnapshot"/> for use
/// inside a list. Row view models are cheap, non-editable and refresh
/// themselves via <see cref="Update"/> when the underlying snapshot
/// changes. <see cref="OpenPage"/> routes through
/// <see cref="ISetupCoordinator"/>; <see cref="UndoableDelete"/> hands
/// the row back to the owning list view model via the
/// <c>requestDelete</c> callback so the list can run its pending-delete
/// undo window before finalizing.
/// </summary>
public sealed class SetupRowViewModel : ListItemRowViewModelBase
{
    private readonly ISetupCoordinator? setupCoordinator;
    private readonly Action<SetupRowViewModel>? requestDelete;
    private Guid? boardId;

    public Guid Id { get; private set; }

    public Guid? BoardId
    {
        get => boardId;
        private set => SetProperty(ref boardId, value);
    }

    public SetupRowViewModel()
    {
        setupCoordinator = null;
        requestDelete = null;
    }

    public SetupRowViewModel(
        SetupSnapshot snapshot,
        ISetupCoordinator setupCoordinator,
        Action<SetupRowViewModel> requestDelete)
    {
        this.setupCoordinator = setupCoordinator;
        this.requestDelete = requestDelete;
        Update(snapshot);
    }

    public void Update(SetupSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        BoardId = snapshot.BoardId;
        Timestamp = null;
        IsComplete = true;
    }

    protected override async Task OpenPageAsync()
    {
        if (setupCoordinator is null) return;
        await setupCoordinator.OpenEditAsync(Id);
    }

    protected override void UndoableDelete()
    {
        requestDelete?.Invoke(this);
    }
}
