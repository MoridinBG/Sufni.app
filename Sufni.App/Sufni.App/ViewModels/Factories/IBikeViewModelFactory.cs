using Sufni.App.Models;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public interface IBikeViewModelFactory
{
    BikeViewModel Create(Bike bike, bool fromDatabase, IBikeViewModelHost host);
}
