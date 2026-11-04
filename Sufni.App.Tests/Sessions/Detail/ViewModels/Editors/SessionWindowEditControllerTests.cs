using NSubstitute;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class SessionWindowEditControllerTests
{
    [Fact]
    public async Task TrimUntilEnd_WhenEditFails_ShowsError()
    {
        using var harness = new SessionPlotActionsFixture();
        harness.EditingCoordinator.TrimUntilEndAsync(harness.SessionId, 4)
            .Returns(new SessionEditResult.Failed(SessionEditFailureReason.Rejected, "Session changed while editing."));

        await harness.WindowEdits.TrimUntilEndCommand
            .ExecuteAsync(CreatePlotContext(clickSeconds: 4));

        Assert.NotEmpty(harness.Errors);
        Assert.Empty(harness.Notifications);
    }

    [Fact]
    public async Task TrimUntilEnd_WhenAppliedButProcessingFails_PreservesDiagnosticDetails()
    {
        using var harness = new SessionPlotActionsFixture();
        const string details = "calibration diagnostic";
        harness.EditingCoordinator.TrimUntilEndAsync(harness.SessionId, 4)
            .Returns(new SessionEditResult.Applied(new SessionRecomputeResult.ProcessingFailed(
                SessionProcessingFailureReason.InvalidCalibration, details)));

        await harness.WindowEdits.TrimUntilEndCommand
            .ExecuteAsync(CreatePlotContext(clickSeconds: 4));

        var failure = Assert.Single(harness.Errors);
        Assert.Equal(details, failure.Details);
        Assert.DoesNotContain(details, failure.Summary);
    }

    [Fact]
    public void UndoTrim_IsAvailableOnlyAfterTrim()
    {
        using var harness = new SessionPlotActionsFixture();
        var command = harness.WindowEdits.UndoTrimCommand;
        Assert.False(command.CanExecute(CreatePlotContext(3)));
        harness.State.OnNext(harness.State.Value with
        {
            Session = harness.State.Value.Session! with { PreviousWindowStartSeconds = 0 },
        });
        Assert.True(command.CanExecute(CreatePlotContext(3)));
    }

    [Fact]
    public async Task RunningEdit_DisablesEveryWindowEditUntilCompletion()
    {
        using var harness = new SessionPlotActionsFixture();
        harness.State.OnNext(harness.State.Value with { Session = harness.State.Value.Session! with { PreviousWindowStartSeconds = 0 } });
        var pending = new TaskCompletionSource<SessionEditResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.EditingCoordinator.TrimUntilEndAsync(harness.SessionId, 4).Returns(pending.Task);
        var commands = new[] { harness.WindowEdits.TrimFromBeginningCommand, harness.WindowEdits.TrimUntilEndCommand,
            harness.WindowEdits.SplitCommand, harness.WindowEdits.UndoTrimCommand };
        var context = CreatePlotContext(4);
        Assert.All(commands, command => Assert.True(command.CanExecute(context)));

        var operation = harness.WindowEdits.TrimUntilEndCommand.ExecuteAsync(context);

        Assert.All(commands, command => Assert.False(command.CanExecute(context)));
        pending.SetResult(new SessionEditResult.Applied());
        await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.All(commands, command => Assert.True(command.CanExecute(context)));
    }

    [Fact]
    public async Task Split_AfterLifetimeCancellationDoesNotNavigateOrReport()
    {
        using var harness = new SessionPlotActionsFixture();
        var pending = new TaskCompletionSource<SessionEditResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<SessionEditProgress>? progress = null;
        harness.EditingCoordinator.SplitAsync(harness.SessionId, 3, Arg.Any<IProgress<SessionEditProgress>>()).Returns(call =>
        {
            progress = call.Arg<IProgress<SessionEditProgress>>();
            return pending.Task;
        });
        var operation = harness.WindowEdits.SplitCommand.ExecuteAsync(CreatePlotContext(3));
        harness.Lifetime.Cancel();

        progress!.Report(new SessionEditProgress(Guid.NewGuid()));
        pending.SetResult(new SessionEditResult.Applied());
        await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        harness.Shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>(), Arg.Any<bool>());
        Assert.Empty(harness.Notifications);
        Assert.Empty(harness.Errors);
    }

    private static TelemetryPlotContextMenuContext CreatePlotContext(double clickSeconds) => SessionPlotActionsFixture.Context(clickSeconds);
}
