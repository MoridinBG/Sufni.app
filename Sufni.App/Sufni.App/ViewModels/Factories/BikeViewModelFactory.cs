using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public class BikeViewModelFactory(IDatabaseService databaseService, IFilesService filesService) : IBikeViewModelFactory
{
    public BikeViewModel Create(Bike bike, bool fromDatabase)
        => new(bike, fromDatabase, databaseService, filesService);
}
