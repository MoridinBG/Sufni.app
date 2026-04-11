using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="PairedDeviceSnapshot"/>
/// for use inside the paired-devices side panel. Refreshes itself via
/// <see cref="Update"/> when the underlying snapshot changes.
/// <see cref="UndoableDelete"/> hands the row back to the owning list
/// view model via the <c>requestDelete</c> callback so the list can run
/// its pending-delete undo window before finalizing.
/// </summary>
public partial class PairedDeviceRowViewModel : ObservableObject, IListItemRow
{
    private readonly Action<PairedDeviceRowViewModel>? requestDelete;

    public string DeviceId { get; private set; }
    public string? DisplayName { get; private set; }

    [ObservableProperty] private DateTime expires;

    /// <summary>
    /// The control template binds <c>Text="{Binding Name}"</c>. Prefer
    /// the human-readable <see cref="DisplayName"/> when present, fall
    /// back to the opaque <see cref="DeviceId"/> when it is null,
    /// empty, or whitespace (older rows stored before the
    /// <c>display_name</c> column existed have a NULL display name).
    /// </summary>
    public string? Name =>
        string.IsNullOrWhiteSpace(DisplayName) ? DeviceId : DisplayName;

    public DateTime? Timestamp => Expires;
    public bool IsComplete => true;

    public PairedDeviceRowViewModel()
    {
        DeviceId = string.Empty;
        requestDelete = null;
    }

    public PairedDeviceRowViewModel(
        PairedDeviceSnapshot snapshot,
        Action<PairedDeviceRowViewModel> requestDelete)
    {
        this.requestDelete = requestDelete;
        DeviceId = snapshot.DeviceId;
        DisplayName = snapshot.DisplayName;
        Expires = snapshot.Expires;
    }

    public void Update(PairedDeviceSnapshot snapshot)
    {
        DeviceId = snapshot.DeviceId;
        DisplayName = snapshot.DisplayName;
        Expires = snapshot.Expires;
        OnPropertyChanged(nameof(Timestamp));
        OnPropertyChanged(nameof(Name));
    }

    [RelayCommand]
    private void UndoableDelete()
    {
        requestDelete?.Invoke(this);
    }

    [RelayCommand]
    private void OpenPage()
    {
        // Paired devices have no detail page; this stub exists so the
        // shared list-row controls can bind a non-null command.
    }

    [RelayCommand]
    private void FakeDelete()
    {
        // Stub for the editor button-strip surface. Paired devices have
        // no editor, but IListItemRow requires the command.
    }

    IRelayCommand IListItemRow.OpenPageCommand => OpenPageCommand;
    IRelayCommand IListItemRow.UndoableDeleteCommand => UndoableDeleteCommand;
    IRelayCommand IListItemRow.FakeDeleteCommand => FakeDeleteCommand;
}
