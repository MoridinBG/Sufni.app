global using Sufni.App.Tests.TestSupport;

using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.Kinematics;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Setups.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.ViewModels.Editors;
namespace Sufni.App.Tests.TestSupport;

internal static class TestCoordinatorSubstitutes
{
    public static IBikeCoordinator Bike()
    {
        var coordinator = Substitute.For<IBikeCoordinator>();

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

    public static ISetupCoordinator Setup()
    {
        var coordinator = Substitute.For<ISetupCoordinator>();

        coordinator.OpenCreateAsync(Arg.Any<Guid?>()).Returns(Task.CompletedTask);
        coordinator.OpenCreateForDetectedBoardAsync().Returns(Task.CompletedTask);
        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.ImportSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupImportResult>(new SetupImportResult.Canceled()));
        coordinator.ExportSetupAsync(Arg.Any<Setup>(), Arg.Any<Bike>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupExportResult>(new SetupExportResult.Canceled()));

        return coordinator;
    }

    public static ITrackCoordinator Track()
    {
        var coordinator = Substitute.For<ITrackCoordinator>();

        coordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GpxImportResult(0, 0)));

        return coordinator;
    }

    public static ISessionCoordinator Session()
    {
        var coordinator = Substitute.For<ISessionCoordinator>();

        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource()));

        return coordinator;
    }

    public static ILiveDaqCoordinator LiveDaq()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();

        coordinator.SelectAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        coordinator.OpenSessionAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static IImportSessionsCoordinator ImportSessions()
    {
        var coordinator = Substitute.For<IImportSessionsCoordinator>();

        coordinator.OpenAsync().Returns(Task.CompletedTask);

        return coordinator;
    }

    public static ISyncCoordinator Sync()
    {
        var coordinator = Substitute.For<ISyncCoordinator>();

        coordinator.SyncAllAsync().Returns(Task.CompletedTask);

        return coordinator;
    }
}
