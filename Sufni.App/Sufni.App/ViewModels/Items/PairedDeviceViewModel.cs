using System;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Hosts;

namespace Sufni.App.ViewModels.Items;

public partial class PairedDeviceViewModel : ItemViewModelBase
{
    #region Constructors

    public PairedDeviceViewModel()
    {
        Name =  "Paired Device";
        Timestamp = DateTime.Now;
    }

    public PairedDeviceViewModel(PairedDevice pairedDevice, INavigator navigator, IDialogService dialogService, IItemDeletionHost deletionHost)
        : base(navigator, dialogService, deletionHost)
    {
        Name = pairedDevice.DeviceId;
        Timestamp = pairedDevice.Expires;
    }

    #endregion
}