using System;
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
public sealed class PairedDeviceRowViewModel : ListItemRowViewModelBase
{
    private readonly Action<PairedDeviceRowViewModel> requestDelete;
    private DateTime expires;

    public string DeviceId { get; private set; }
    public string? DisplayName { get; private set; }

    public DateTime Expires
    {
        get => expires;
        private set => SetProperty(ref expires, value);
    }

    public PairedDeviceRowViewModel(
        PairedDeviceSnapshot snapshot,
        Action<PairedDeviceRowViewModel> requestDelete)
    {
        this.requestDelete = requestDelete;
        DeviceId = snapshot.DeviceId;
        DisplayName = snapshot.DisplayName;
        Expires = snapshot.Expires;
        Name = GetName();
        Timestamp = Expires;
        IsComplete = true;
    }

    public void Update(PairedDeviceSnapshot snapshot)
    {
        DeviceId = snapshot.DeviceId;
        DisplayName = snapshot.DisplayName;
        Expires = snapshot.Expires;
        Name = GetName();
        Timestamp = Expires;
        IsComplete = true;
    }

    protected override void UndoableDelete()
    {
        requestDelete(this);
    }

    private string GetName() =>
        string.IsNullOrWhiteSpace(DisplayName) ? DeviceId : DisplayName;
}
