using System;
using System.Linq;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public class BikeUsageQuery(Func<SetupListViewModel> setupListProvider) : IBikeUsageQuery
{
    public bool IsBikeInUse(Guid bikeId) => setupListProvider().Items.Any(s =>
        s is SetupViewModel { SelectedBike: not null } svm &&
        svm.SelectedBike.Id == bikeId);
}
