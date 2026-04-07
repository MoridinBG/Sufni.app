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
/// when the underlying snapshot changes. Open/delete commands route
/// through <see cref="ISessionCoordinator"/>.
/// </summary>
public partial class SessionRowViewModel : ObservableObject, IListItemRow
{
    private readonly ISessionCoordinator? sessionCoordinator;

    public Guid Id { get; private set; }

    [ObservableProperty] private string? name;
    [ObservableProperty] private DateTime? timestamp;
    [ObservableProperty] private bool isComplete;

    public SessionRowViewModel()
    {
        sessionCoordinator = null;
    }

    public SessionRowViewModel(SessionSnapshot snapshot, ISessionCoordinator sessionCoordinator)
    {
        this.sessionCoordinator = sessionCoordinator;
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
    private async Task UndoableDelete()
    {
        if (sessionCoordinator is null) return;
        await sessionCoordinator.DeleteAsync(Id);
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
