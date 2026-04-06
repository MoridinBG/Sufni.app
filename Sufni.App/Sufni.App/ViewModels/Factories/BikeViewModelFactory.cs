using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public class BikeViewModelFactory(
    IDatabaseService databaseService,
    IFilesService filesService,
    INavigator navigator,
    IDialogService dialogService) : IBikeViewModelFactory
{
    public BikeViewModel Create(Bike bike, bool fromDatabase, IBikeViewModelHost host)
        => new(bike, fromDatabase, databaseService, filesService, navigator, dialogService, host);
}
