using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="SessionSnapshot"/> for use
/// inside the session list. Refreshes itself via <see cref="Update"/>
/// when the underlying snapshot changes. <see cref="OpenPage"/> routes
/// through <see cref="ISessionCoordinator"/>;
/// <see cref="UndoableDelete"/> hands the row back to the owning list
/// view model via the <c>requestDelete</c> callback so the list can run
/// its pending-delete undo window before finalizing.
/// </summary>
public partial class SessionRowViewModel : ObservableObject, IListItemRow
{
    private readonly ISessionCoordinator? sessionCoordinator;
    private readonly Action<SessionRowViewModel>? requestDelete;

    public Guid Id { get; private set; }

    [ObservableProperty] private string? name;
    [ObservableProperty] private DateTime? timestamp;
    [ObservableProperty] private bool isComplete;

    public SessionRowViewModel()
    {
        sessionCoordinator = null;
        requestDelete = null;
    }

    public SessionRowViewModel(
        SessionSnapshot snapshot,
        ISessionCoordinator sessionCoordinator,
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

    [RelayCommand]
    private async Task OpenPage()
    {
        if (sessionCoordinator is null) return;
        await sessionCoordinator.OpenEditAsync(Id);
    }

    [RelayCommand]
    private void UndoableDelete()
    {
        requestDelete?.Invoke(this);
    }

    [RelayCommand]
    private void FakeDelete()
    {
        // Exists so the controls can bind to a delete command on this row.
    }

    IRelayCommand IListItemRow.OpenPageCommand => OpenPageCommand;
    IRelayCommand IListItemRow.UndoableDeleteCommand => UndoableDeleteCommand;
    IRelayCommand IListItemRow.FakeDeleteCommand => FakeDeleteCommand;
}
