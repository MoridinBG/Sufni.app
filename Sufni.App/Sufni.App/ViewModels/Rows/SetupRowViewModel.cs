using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public partial class SetupRowViewModel : ObservableObject, IListItemRow
{
    private readonly ISetupCoordinator? setupCoordinator;
    private readonly Action<SetupRowViewModel>? requestDelete;

    public Guid Id { get; private set; }

    [ObservableProperty] private string? name;
    [ObservableProperty] private Guid? boardId;

    // Stub members for IListItemRow. Setups have no timestamp or
    // sync-completion concept on a row; the controls just hide them.
    public DateTime? Timestamp => null;
    public bool IsComplete => true;

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
    }

    [RelayCommand]
    private async Task OpenPage()
    {
        if (setupCoordinator is null) return;
        await setupCoordinator.OpenEditAsync(Id);
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
