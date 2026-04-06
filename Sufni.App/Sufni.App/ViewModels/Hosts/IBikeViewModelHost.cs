using System;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Hosts;

public interface IBikeViewModelHost : IItemDeletionHost
{
    bool CanDeleteBike(Guid bikeId);
    void OnBikeSaved(BikeViewModel vm);
}
