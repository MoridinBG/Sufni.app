using System;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

// Inherits from ItemListViewModelBase for the shared search-bar /
// date-filter / menu-item state. The items collection is owned locally
// — `pairedDeviceRows` is a typed projection from the store, exposed
// via the `new` shadow on `Items`.
public partial class PairedDeviceListViewModel : ItemListViewModelBase
{
    #region Private fields

    private readonly IPairedDeviceStore pairedDeviceStore;
    private readonly IPairedDeviceCoordinator pairedDeviceCoordinator;
    private readonly ReadOnlyObservableCollection<PairedDeviceRowViewModel> pairedDeviceRows;
    private readonly BehaviorSubject<Func<PairedDeviceSnapshot, bool>> filterSubject = new(_ => true);
    private (string DeviceId, string Name)? pendingDelete;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<PairedDeviceRowViewModel> Items => pairedDeviceRows;

    #endregion Observable properties

    #region Constructors

    public PairedDeviceListViewModel()
    {
        pairedDeviceStore = null!;
        pairedDeviceCoordinator = null!;
        pairedDeviceRows = new ReadOnlyObservableCollection<PairedDeviceRowViewModel>([]);
    }

    public PairedDeviceListViewModel(
        IPairedDeviceStore pairedDeviceStore,
        IPairedDeviceCoordinator pairedDeviceCoordinator)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        this.pairedDeviceCoordinator = pairedDeviceCoordinator;

        pairedDeviceStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new PairedDeviceRowViewModel(snapshot, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out pairedDeviceRows)
            .Subscribe();
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void RebuildFilter()
    {
        var pendingId = pendingDelete?.DeviceId;
        filterSubject.OnNext(snapshot =>
            pendingId is null || snapshot.DeviceId != pendingId);
    }

    protected override void OnPendingDeleteUndone()
    {
        pendingDelete = null;
        RebuildFilter();
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void RequestRowDelete(PairedDeviceRowViewModel row)
    {
        _ = RunActionSwallowExceptionToErrorMessages(async () =>
        {
            var snapshot = pairedDeviceStore.Get(row.DeviceId);
            if (snapshot is null) return;

            await FlushPendingDeleteAsync();

            var displayName =
                string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.DeviceId : snapshot.DisplayName!;
            pendingDelete = (snapshot.DeviceId, displayName);
            RebuildFilter();

            StartUndoWindow(displayName, () => FinalizeUnpairAsync(snapshot.DeviceId));
        });
    }

    private async Task FinalizeUnpairAsync(string deviceId)
    {
        pendingDelete = null;
        RebuildFilter();

        var result = await pairedDeviceCoordinator.UnpairAsync(deviceId);
        if (result is PairedDeviceUnpairResult.Failed failed)
        {
            ErrorMessages.Add(failed.ErrorMessage);
        }
    }

    #endregion Private methods
}
