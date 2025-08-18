using System;
using Sufni.App.Models;

namespace Sufni.App.ViewModels.Items;

public partial class PairedDeviceViewModel : ItemViewModelBase
{
    #region Constructors

    public PairedDeviceViewModel()
    {
        Name =  "Paired Device";
        Timestamp = DateTime.Now;
    }

    public PairedDeviceViewModel(PairedDevice pairedDevice)
    {
        Name = pairedDevice.DeviceId;
        Timestamp = pairedDevice.Expires;
    }

    #endregion
}