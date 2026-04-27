using System;
using System.Threading.Tasks;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="SessionSnapshot"/> for use
/// inside the session list. Refreshes itself via <see cref="Update"/>
/// when the underlying snapshot changes. <see cref="OpenPage"/> routes
/// through <see cref="SessionCoordinator"/>;
/// <see cref="UndoableDelete"/> hands the row back to the owning list
/// view model via the <c>requestDelete</c> callback so the list can run
/// its pending-delete undo window before finalizing.
/// </summary>
public sealed class SessionRowViewModel : ListItemRowViewModelBase
{
    private readonly SessionCoordinator sessionCoordinator;
    private readonly Action<SessionRowViewModel> requestDelete;

    public Guid Id { get; private set; }

    public SessionRowViewModel(
        SessionSnapshot snapshot,
        SessionCoordinator sessionCoordinator,
        Action<SessionRowViewModel> requestDelete)
    {
        this.sessionCoordinator = sessionCoordinator;
        this.requestDelete = requestDelete;
        Update(snapshot);
    }

    public void Update(SessionSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        Timestamp = snapshot.Timestamp is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(snapshot.Timestamp.Value).LocalDateTime;
        IsComplete = snapshot.HasProcessedData;
    }

    protected override async Task OpenPageAsync()
    {
        await sessionCoordinator.OpenEditAsync(Id);
    }

    protected override void UndoableDelete()
    {
        requestDelete(this);
    }
}
