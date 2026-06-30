using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels.Rows;
namespace Sufni.App.SyncAndPairing.ViewModels.ItemLists;

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
    private readonly HashSet<string> pendingDeleteIds = [];

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<PairedDeviceRowViewModel> Items => pairedDeviceRows;

    #endregion Observable properties

    #region Constructors

    public PairedDeviceListViewModel(
        IPairedDeviceStore pairedDeviceStore,
        IPairedDeviceCoordinator pairedDeviceCoordinator,
        IUiThreadDispatcher uiThreadDispatcher,
        IBackgroundTaskRunner? backgroundTaskRunner = null)
        : base(uiThreadDispatcher, backgroundTaskRunner)
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
        var pendingIds = pendingDeleteIds.Count == 0 ? null : new HashSet<string>(pendingDeleteIds);
        filterSubject.OnNext(snapshot =>
            pendingIds is null || !pendingIds.Contains(snapshot.DeviceId));
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void RequestRowDelete(PairedDeviceRowViewModel row)
    {
        var snapshot = pairedDeviceStore.Get(row.DeviceId);
        if (snapshot is null) return;

        var displayName =
            string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.DeviceId : snapshot.DisplayName!;
        pendingDeleteIds.Add(snapshot.DeviceId);
        RebuildFilter();

        StartUndoWindow(
            displayName,
            finalize: () => FinalizeUnpairAsync(snapshot.DeviceId),
            onUndone: () => OnUnpairUndone(snapshot.DeviceId));
    }

    private void OnUnpairUndone(string deviceId)
    {
        pendingDeleteIds.Remove(deviceId);
        RebuildFilter();
    }

    private async Task FinalizeUnpairAsync(string deviceId)
    {
        var result = await pairedDeviceCoordinator.UnpairAsync(deviceId);

        pendingDeleteIds.Remove(deviceId);
        RebuildFilter();

        if (result is PairedDeviceUnpairResult.Failed failed)
        {
            ErrorMessages.Add(failed.ErrorMessage);
        }
    }

    #endregion Private methods
}
