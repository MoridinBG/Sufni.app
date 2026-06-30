global using Sufni.App.Tests.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SetupEditing;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Infrastructure;

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
