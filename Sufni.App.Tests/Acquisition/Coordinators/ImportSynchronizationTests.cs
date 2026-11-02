using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Acquisition;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sync;
using static Sufni.App.Tests.TestSupport.Fixtures.TestTelemetrySources;

namespace Sufni.App.Tests.Acquisition.Coordinators;

public class ImportSynchronizationTests
{
    [Fact]
    public async Task IndependentImports_ConvergeToOneSessionOnBothDevicesAfterSync()
    {
        var dispatcher = new InlineUiThreadDispatcher();
        await using var first = new InboundSyncFixture(dispatcher, inMemory: true);
        await using var second = new InboundSyncFixture(dispatcher, inMemory: true);
        var harness = new ImportWorkflowHarness();
        var (setup, _) = harness.SeedSetupAndBike();
        var bytes = TestSstFiles.CreateValidV3();
        using var firstImport = CreateImport(first);
        using var secondImport = CreateImport(second);

        await firstImport.ImportAsync(CreateDataStore(), [CreateImportItem(name: "first", sourceBytes: bytes)],
            setup.Id, TestContext.Current.CancellationToken);
        second.Time.Advance(TimeSpan.FromSeconds(1));
        await secondImport.ImportAsync(CreateDataStore(), [CreateImportItem(name: "renamed", sourceBytes: bytes)],
            setup.Id, TestContext.Current.CancellationToken);

        Assert.Single(Assert.IsType<SessionImportState.Completed>(firstImport.State).Result.Imported);
        Assert.Single(Assert.IsType<SessionImportState.Completed>(secondImport.State).Result.Imported);
        var firstRow = Assert.Single(await first.Database.SessionRepository.GetSessionsAsync());
        var secondRow = Assert.Single(await second.Database.SessionRepository.GetSessionsAsync());
        Assert.Equal(firstRow.Id, secondRow.Id);
        Assert.Equal("first", firstRow.Name);
        Assert.Equal("renamed", secondRow.Name);

        var push = await second.Database.SyncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Push, 0, long.MaxValue);
        await first.CreateRowApplier().ApplyAsync(push, SynchronizationDirection.Push);
        var pull = await first.Database.SyncDataStore.GetSynchronizationDataAsync(SynchronizationDirection.Pull, 0, long.MaxValue);
        await second.CreateRowApplier().ApplyAsync(pull, SynchronizationDirection.Pull);

        foreach (var device in new[] { first, second })
        {
            var persisted = Assert.Single(await device.Database.SessionRepository.GetSessionsAsync());
            Assert.Equal(firstRow.Id, persisted.Id);
            Assert.Equal("renamed", persisted.Name);
            Assert.Equal("renamed", device.Sessions.Get(firstRow.Id)!.Name);
            Assert.Equal(secondRow.Version, persisted.Version);
            Assert.Equal(secondRow.Updated, persisted.Updated);
        }

        ImportSessionsCoordinator CreateImport(InboundSyncFixture device) => new(
            device.Database.SessionRepository, device.Database.TrackRepository,
            harness.SetupRepository, harness.BikeRepository, device.Sessions, device.Sources,
            harness.BackgroundTaskRunner, harness.Reprocessor, dispatcher, device.Publication,
            device.Tracks, harness.TelemetryDataStoreService, harness.FilesService,
            TestAppEnvironment.Create(UiLayoutProfile.Workspace));
    }
}
