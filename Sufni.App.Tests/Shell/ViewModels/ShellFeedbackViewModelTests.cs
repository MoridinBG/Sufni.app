using NSubstitute;
using System.Reactive.Subjects;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Shared.Models;
using Sufni.App.Video.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Sessions.Coordination;

namespace Sufni.App.Tests.Shell.ViewModels;

public class ShellFeedbackViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulkRecompute_ReportsPartialFailureAndSeparatesExceptionalDiagnostics(bool exceptional)
    {
        var sessions = TestCoordinatorSubstitutes.Session();
        using var feedback = TestShellFeedback.Create(sessions: sessions);
        const string details = "database inaccessible";
        SessionRecomputeAllOutcome outcome = exceptional
            ? new SessionRecomputeAllOutcome.Failed(SessionRecomputeFailureReason.RecomputeAll, details)
            : new SessionRecomputeAllOutcome.Completed(new SessionRecomputeAllResult(4, 2, 0, 1, 1));

        sessions.RecomputeAllFinished += Raise.Event<EventHandler<SessionRecomputeAllOutcome>>(sessions, outcome);

        var failure = Assert.Single(feedback.ErrorMessages);
        if (exceptional)
        {
            Assert.Equal(details, failure.Details);
            Assert.DoesNotContain(details, failure.Summary);
        }
    }

    [Fact]
    public void MissingServer_ShowsNotificationWithoutFailure()
    {
        var sync = TestCoordinatorSubstitutes.Sync();
        using var feedback = TestShellFeedback.Create(sync);

        sync.ServerUnavailable += Raise.Event();

        Assert.Single(feedback.Notifications);
        Assert.Empty(feedback.ErrorMessages);
    }

    [Fact]
    public void VideoWithoutMatchingSession_ShowsNotificationInsteadOfFailure()
    {
        using var feedback = TestShellFeedback.Create();

        feedback.ShowVideoResult(new VideoImportResult.NoMatchingSession());

        Assert.Single(feedback.Notifications);
        Assert.Empty(feedback.ErrorMessages);
    }

    [Fact]
    public void PartialGpxImport_ReportsSavedTracks_AndSeparatesFailureDiagnostics()
    {
        using var feedback = TestShellFeedback.Create();
        const string details = "XML diagnostic";

        feedback.ShowGpxResult(new GpxImportResult.Failed(GpxImportFailureReason.InvalidFile, details, ImportedCount: 1));

        Assert.Single(feedback.Notifications);
        var failure = Assert.Single(feedback.ErrorMessages);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
    }

    [Fact]
    public void SyncProgress_TracksTheCurrentRunAndClearsWhenItEnds()
    {
        using var states = new BehaviorSubject<SyncState>(new(PairingClientState.Unpaired, SyncActivity.Idle));
        var sync = TestCoordinatorSubstitutes.Sync(states: states);
        using var feedback = TestShellFeedback.Create(sync);
        var observedProgress = new List<double>();
        feedback.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(feedback.SyncProgressValue)) observedProgress.Add(feedback.SyncProgressValue);
        };
        states.OnNext(new(PairingClientState.Paired, SyncActivity.Outbound,
            new SynchronizationProgressSnapshot(SynchronizationPhase.PushingLocalChanges, "receiving", 3, 8, true)));

        Assert.True(feedback.SyncInProgress);
        Assert.Equal(0.375, feedback.SyncProgressValue);
        Assert.False(feedback.SyncProgressIsIndeterminate);

        states.OnNext(new(PairingClientState.Paired, SyncActivity.Idle));

        Assert.False(feedback.SyncInProgress);
        Assert.Equal(0, feedback.SyncProgressValue);
        Assert.Empty(feedback.SyncProgressText);
        Assert.Equal([0.375, 0], observedProgress);
    }

    [Fact]
    public void SuccessfulSync_ReplacesGlobalFailureWithNotification()
    {
        var sync = TestCoordinatorSubstitutes.Sync();
        using var feedback = TestShellFeedback.Create(sync);
        sync.SyncFailed += Raise.Event<EventHandler<SyncFailedEventArgs>>(sync, new SyncFailedEventArgs(SyncFailureReason.Synchronize, "failed"));
        Assert.Single(feedback.ErrorMessages);

        sync.SyncCompleted += Raise.Event<EventHandler<SyncCompletedEventArgs>>(sync, new SyncCompletedEventArgs(new SynchronizationRunResult.Completed()));

        Assert.Empty(feedback.ErrorMessages);
        Assert.Single(feedback.Notifications);
    }

    [Fact]
    public async Task SyncCommand_TracksAvailabilityAndRunsTheCoordinator()
    {
        using var states = new BehaviorSubject<SyncState>(new(PairingClientState.Unpaired, SyncActivity.Idle));
        var sync = TestCoordinatorSubstitutes.Sync(states: states);
        using var feedback = TestShellFeedback.Create(sync);
        Assert.False(feedback.SyncCommand.CanExecute(null));
        bool? observedAvailability = null;
        feedback.SyncCommand.CanExecuteChanged += (_, _) => observedAvailability = feedback.SyncCommand.CanExecute(null);
        states.OnNext(new(PairingClientState.Paired, SyncActivity.Idle));
        Assert.True(observedAvailability);

        await feedback.SyncCommand.ExecuteAsync(null);

        await sync.Received(1).SyncAllAsync();
    }
}
