global using Sufni.App.Tests.TestSupport;
using System.Reactive.Linq;
using Sufni.App.Setups.Queries;

using Sufni.App.Tests.TestSupport.Fixtures;

using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Setups.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.ViewModels.Editors;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal static class TestCoordinatorSubstitutes
{
    public static IBikeCoordinator Bike()
    {
        var coordinator = Substitute.For<IBikeCoordinator>();

        coordinator.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.NotApplicable()));
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

        coordinator.ImportSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupImportResult>(new SetupImportResult.Canceled()));
        coordinator.ExportSetupAsync(Arg.Any<Setup>(), Arg.Any<Bike>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupExportResult>(new SetupExportResult.Canceled()));

        coordinator.CreateDraft(Arg.Any<Guid?>()).Returns(call =>
            new SetupDetails(TestSnapshots.Setup(), call.Arg<Guid?>()));
        return coordinator;
    }

    public static ITrackCoordinator Track()
    {
        var coordinator = Substitute.For<ITrackCoordinator>();

        coordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GpxImportResult>(new GpxImportResult.Canceled()));

        return coordinator;
    }

    public static ISessionCoordinator Session() => Substitute.For<ISessionCoordinator>();

    public static ISessionLoader SessionLoader()
    {
        var loader = Substitute.For<ISessionLoader>();
        loader.LoadTrackAsync(
                Arg.Any<Guid>(),
                Arg.Any<TelemetryData>(),
                Arg.Any<IProgress<SessionDetailLoadProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(new SessionDetailTrackLoadResult.Loaded(
                new SessionTrackPresentationData(null, null, null)));
        return loader;
    }

    public static ISessionRecomputeEngine RecomputeEngine()
    {
        var engine = Substitute.For<ISessionRecomputeEngine>();
        engine.RequestRecomputeAsync(Arg.Any<Guid>())
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource()));
        return engine;
    }

    public static ILiveDaqCoordinator LiveDaq()
    {
        var coordinator = Substitute.For<ILiveDaqCoordinator>();
        coordinator.ObserveManagementAvailability(Arg.Any<string>())
            .Returns(Observable.Return(new LiveDaqManagementAvailability(null, null)));
        return coordinator;
    }

    public static IImportSessionsCoordinator ImportSessions()
    {
        var coordinator = Substitute.For<IImportSessionsCoordinator>();

        coordinator.State.Returns(new SessionImportState.Idle());
        coordinator.Observe().Returns(System.Reactive.Linq.Observable.Return<SessionImportState>(new SessionImportState.Idle()));

        return coordinator;
    }

    public static ISyncCoordinator Sync(Sufni.App.SyncAndPairing.Models.SyncState? state = null,
        System.Reactive.Subjects.BehaviorSubject<Sufni.App.SyncAndPairing.Models.SyncState>? states = null)
    {
        var coordinator = Substitute.For<ISyncCoordinator>();

        var initial = state ?? new Sufni.App.SyncAndPairing.Models.SyncState(
            Sufni.App.SyncAndPairing.Models.PairingClientState.Unpaired, Sufni.App.SyncAndPairing.Services.SyncActivity.Idle);
        coordinator.State.Returns(_ => states?.Value ?? initial);
        coordinator.States.Returns(states ?? System.Reactive.Linq.Observable.Return(initial));
        coordinator.SyncAllAsync().Returns(Task.CompletedTask);

        return coordinator;
    }
}
