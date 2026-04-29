global using Sufni.App.Tests.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Infrastructure;

internal static class TestCoordinatorSubstitutes
{
    public static BikeCoordinator Bike()
    {
        var coordinator = Substitute.For<BikeCoordinator>(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<IDatabaseService>(),
            Substitute.For<IBikeDependencyQuery>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IBikeEditorService>(),
            Substitute.For<IDialogService>());

        coordinator.OpenCreateAsync().Returns(Task.CompletedTask);
        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.LoadAnalysisAsync(Arg.Any<RearSuspension?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        coordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        coordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        coordinator.ImportLeverageRatioAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LeverageRatioImportResult>(new LeverageRatioImportResult.Canceled()));
        coordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));

        return coordinator;
    }

    public static SetupCoordinator Setup()
    {
        var coordinator = Substitute.For<SetupCoordinator>(
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<IBikeStore>(),
            Bike(),
            Substitute.For<IDatabaseService>(),
            Substitute.For<ITelemetryDataStoreService>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>());

        coordinator.OpenCreateAsync(Arg.Any<Guid?>()).Returns(Task.CompletedTask);
        coordinator.OpenCreateForDetectedBoardAsync().Returns(Task.CompletedTask);
        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static TrackCoordinator Track()
    {
        var coordinator = Substitute.For<TrackCoordinator>(
            Substitute.For<IDatabaseService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IBackgroundTaskRunner>());

        coordinator.ImportGpxAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static SessionCoordinator Session()
    {
        var coordinator = Substitute.For<SessionCoordinator>(
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IDatabaseService>(),
            Substitute.For<IHttpApiService>(),
            Substitute.For<IBackgroundTaskRunner>(),
            Track(),
            Substitute.For<ISessionPresentationService>(),
            Substitute.For<ITileLayerService>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>(),
            null);

        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static LiveDaqCoordinator LiveDaq()
    {
        var coordinator = Substitute.For<LiveDaqCoordinator>(
            Substitute.For<ILiveDaqStoreWriter>(),
            Substitute.For<ILiveDaqKnownBoardsQuery>(),
            Substitute.For<ILiveDaqCatalogService>(),
            Substitute.For<ILiveDaqSharedStreamRegistry>(),
            Substitute.For<ILiveSessionServiceFactory>(),
            Session(),
            Substitute.For<ISessionPresentationService>(),
            Substitute.For<IBackgroundTaskRunner>(),
            Substitute.For<ITileLayerService>(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IDialogService>());

        coordinator.SelectAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        coordinator.OpenSessionAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static ImportSessionsCoordinator ImportSessions()
    {
        var coordinator = Substitute.For<ImportSessionsCoordinator>(
            Substitute.For<IDatabaseService>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IBackgroundTaskRunner>(),
            Substitute.For<IDaqManagementService>(),
            new Func<ImportSessionsViewModel>(() => null!));

        coordinator.OpenAsync().Returns(Task.CompletedTask);

        return coordinator;
    }

    public static SyncCoordinator Sync() =>
        new(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IPairedDeviceStoreWriter>(),
            null,
            null);
}
