using NSubstitute;
using Sufni.App.Shared.Models;
using Sufni.App.Video.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shell.ViewModels;

public class ShellFeedbackViewModelTests
{
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
    public void SyncProgress_TracksTheCurrentRunAndClearsWhenItEnds()
    {
        var sync = TestCoordinatorSubstitutes.Sync();
        using var feedback = TestShellFeedback.Create(sync);
        var observedProgress = new List<double>();
        feedback.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(feedback.SyncProgressValue)) observedProgress.Add(feedback.SyncProgressValue);
        };
        sync.IsRunning.Returns(true);
        sync.Progress.Returns(new SynchronizationProgressSnapshot(SynchronizationPhase.ReceivingChanges,
            "receiving", 3, 8, true));
        sync.IsRunningChanged += Raise.Event();

        Assert.True(feedback.SyncInProgress);
        Assert.Equal(0.375, feedback.SyncProgressValue);
        Assert.False(feedback.SyncProgressIsIndeterminate);

        sync.IsRunning.Returns(false);
        sync.Progress.Returns((SynchronizationProgressSnapshot?)null);
        sync.IsRunningChanged += Raise.Event();

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

        sync.SyncCompleted += Raise.Event<EventHandler<SyncCompletedEventArgs>>(sync, new SyncCompletedEventArgs("complete"));

        Assert.Empty(feedback.ErrorMessages);
        Assert.Single(feedback.Notifications);
    }

    [Fact]
    public async Task SyncCommand_TracksAvailabilityAndRunsTheCoordinator()
    {
        var sync = TestCoordinatorSubstitutes.Sync();
        using var feedback = TestShellFeedback.Create(sync);
        Assert.False(feedback.SyncCommand.CanExecute(null));
        bool? observedAvailability = null;
        feedback.SyncCommand.CanExecuteChanged += (_, _) => observedAvailability = feedback.SyncCommand.CanExecute(null);
        sync.CanSync.Returns(true);
        sync.CanSyncChanged += Raise.Event();
        Assert.True(observedAvailability);

        await feedback.SyncCommand.ExecuteAsync(null);

        await sync.Received(1).SyncAllAsync();
    }
}
