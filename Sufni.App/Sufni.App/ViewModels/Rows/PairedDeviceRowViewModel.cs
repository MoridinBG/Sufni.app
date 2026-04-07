using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="PairedDeviceSnapshot"/>
/// for use inside the paired-devices side panel. Refreshes itself via
/// <see cref="Update"/> when the underlying snapshot changes. The
/// unpair command routes through
/// <see cref="IPairedDeviceCoordinator"/>.
///
/// The row owns no error state itself (it inherits from
/// <see cref="ObservableObject"/>, not <see cref="ViewModelBase"/>),
/// so unpair failures are surfaced via the <c>reportError</c> callback
/// that the list view model passes to the constructor. The list VM
/// pushes those messages into its inherited <c>ErrorMessages</c>
/// collection.
/// </summary>
public partial class PairedDeviceRowViewModel : ObservableObject, IListItemRow
{
    private readonly IPairedDeviceCoordinator? coordinator;
    private readonly Action<string>? reportError;

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
        coordinator = null;
        reportError = null;
    }

    public PairedDeviceRowViewModel(
        PairedDeviceSnapshot snapshot,
        IPairedDeviceCoordinator coordinator,
        Action<string> reportError)
    {
        this.coordinator = coordinator;
        this.reportError = reportError;
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
    private async Task UndoableDelete()
    {
        if (coordinator is null) return;
        var result = await coordinator.UnpairAsync(DeviceId);
        if (result is PairedDeviceUnpairResult.Failed failed)
        {
            reportError?.Invoke(failed.ErrorMessage);
        }
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
