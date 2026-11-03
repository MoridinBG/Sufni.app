using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Shell.Coordinators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using Sufni.App.Infrastructure;

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
    private readonly ObservableCollectionExtended<PairedDeviceRowViewModel> pairedDeviceRowsSource = [];
    private readonly ReadOnlyObservableCollection<PairedDeviceRowViewModel> pairedDeviceRows;
    private readonly BehaviorSubject<Func<PairedDeviceSnapshot, bool>> filterSubject = new(_ => true);

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<PairedDeviceRowViewModel> Items => pairedDeviceRows;

    #endregion Observable properties

    #region Constructors

    public PairedDeviceListViewModel(
        IPairedDeviceStore pairedDeviceStore,
        IUiThreadDispatcher uiThreadDispatcher,
        IPendingDeletes pendingDeletes,
        TimeProvider timeProvider)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.pairedDeviceStore = pairedDeviceStore;
        pairedDeviceRows = new ReadOnlyObservableCollection<PairedDeviceRowViewModel>(pairedDeviceRowsSource);
        ObservePendingDeletes<PendingDeleteKey.PairedDevice>(pendingDeletes);
    }

    #endregion Constructors

    #region ItemListViewModelBase overrides

    protected override void AttachSubscriptions(CompositeDisposable subscriptions)
    {
        pairedDeviceRowsSource.Clear();
        RebuildFilter();

        subscriptions.Add(pairedDeviceStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new PairedDeviceRowViewModel(snapshot, RequestRowDelete),
                (row, snapshot) => row.Update(snapshot))
            .Bind(pairedDeviceRowsSource)
            .Subscribe());
    }

    protected override void OnSubscriptionsDetached()
    {
        pairedDeviceRowsSource.Clear();
    }

    protected override void RebuildFilter()
    {
        filterSubject.OnNext(snapshot =>
            !IsPendingDelete(new PendingDeleteKey.PairedDevice(snapshot.DeviceId)));
    }

    #endregion ItemListViewModelBase overrides

    #region Private methods

    private void RequestRowDelete(PairedDeviceRowViewModel row)
    {
        var snapshot = pairedDeviceStore.Get(row.DeviceId);
        if (snapshot is null) return;
        var name = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.DeviceId : snapshot.DisplayName;
        ScheduleDelete(new PendingDeleteKey.PairedDevice(snapshot.DeviceId), name);
    }

    #endregion Private methods
}
