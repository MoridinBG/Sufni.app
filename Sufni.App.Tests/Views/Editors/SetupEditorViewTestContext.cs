using DynamicData;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

internal sealed class SetupEditorViewTestContext : IDisposable
{
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly ISetupCoordinator setupCoordinator = Substitute.For<ISetupCoordinator>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly SourceCache<BikeSnapshot, Guid> bikesCache = new(snapshot => snapshot.Id);

    public SetupEditorViewTestContext()
    {
        bikeStore.Connect().Returns(bikesCache.Connect());
    }

    public IBikeCoordinator BikeCoordinator => bikeCoordinator;

    public BikeSnapshot AddBike(string name = "test bike")
    {
        var bike = TestSnapshots.Bike(name: name);
        bikesCache.AddOrUpdate(bike);
        return bike;
    }

    public SetupEditorViewModel CreateEditor(SetupSnapshot snapshot, bool isNew = false)
    {
        return new SetupEditorViewModel(snapshot, isNew, bikeStore, bikeCoordinator, setupCoordinator, shell, dialogService);
    }

    public static SetupSnapshot CreateSetupSnapshot(BikeSnapshot bike, Guid? boardId = null)
    {
        return TestSnapshots.Setup(bikeId: bike.Id, boardId: boardId ?? Guid.NewGuid()) with
        {
            FrontSensorConfigurationJson = SensorConfiguration.ToJson(new LinearForkSensorConfiguration
            {
                Length = 100,
                Resolution = 12
            }),
            RearSensorConfigurationJson = SensorConfiguration.ToJson(new LinearShockSensorConfiguration
            {
                Length = 75,
                Resolution = 14
            })
        };
    }

    public void Dispose()
    {
        bikesCache.Dispose();
    }
}