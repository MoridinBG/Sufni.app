using System;
using System.Collections.ObjectModel;
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

    private readonly IPairedDeviceCoordinator pairedDeviceCoordinator;
    private readonly ReadOnlyObservableCollection<PairedDeviceRowViewModel> pairedDeviceRows;

    #endregion Private fields

    #region Observable properties

    public ReadOnlyObservableCollection<PairedDeviceRowViewModel> Items => pairedDeviceRows;

    #endregion Observable properties

    #region Constructors

    public PairedDeviceListViewModel()
    {
        pairedDeviceCoordinator = null!;
        pairedDeviceRows = new ReadOnlyObservableCollection<PairedDeviceRowViewModel>([]);
    }

    public PairedDeviceListViewModel(
        IPairedDeviceStore pairedDeviceStore,
        IPairedDeviceCoordinator pairedDeviceCoordinator)
    {
        this.pairedDeviceCoordinator = pairedDeviceCoordinator;

        pairedDeviceStore.Connect()
            .TransformWithInlineUpdate(
                snapshot => new PairedDeviceRowViewModel(snapshot, pairedDeviceCoordinator, ErrorMessages.Add),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out pairedDeviceRows)
            .Subscribe();
    }

    #endregion Constructors
}
